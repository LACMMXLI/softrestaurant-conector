using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RestaurantAgent.Extractor;

internal sealed record DiagnosticCheck(string Name, bool Ok, string Detail);
internal sealed record DiagnosticsReport(bool Ok, IReadOnlyList<DiagnosticCheck> Checks);

/// <summary>Credencial de dispositivo que la GUI obtuvo de central-api (POST /api/web/branches/{code}/link-device o .../replace-device) y entrega al servicio para que la persista.</summary>
internal sealed record LinkDeviceCredential(
    string InstallationId, string BranchCode, string BusinessId, string Token, string? ApiUrl);

/// <summary>Lo que la GUI necesita saber antes de poder iniciar sesión y vincular: dónde está central-api y si este equipo ya tiene identidad.</summary>
internal sealed record AgentControlConfig(
    string? ApiUrl, bool Linked, string BranchCode, string? BusinessId, string? InstallationId, string MachineName);

/// <summary>
/// API HTTP de control local, alcanzable únicamente desde <c>127.0.0.1</c> (nunca desde la red):
/// la consume la GUI de bandeja del mismo equipo. No requiere autenticación a propósito —el
/// alcance es "cualquier proceso de esta máquina", igual que otros agentes locales conocidos
/// (Docker Desktop, etc.)— porque no expone credenciales ni datos sensibles, solo estado y un
/// disparador de sincronización con límite de frecuencia (ver <see cref="SyncCoordinator"/>).
/// </summary>
internal sealed class AgentControlServer(
    ExtractorConfig config, AgentStatusStore statusStore, AgentLog log, SyncCoordinator coordinator)
    : IHostedService
{
    private WebApplication? app;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{config.ControlPort}");
        builder.Logging.ClearProviders();

        app = builder.Build();

        app.MapGet("/status", () => Results.Ok(statusStore.Current));

        app.MapGet("/logs", (int? tail) => Results.Ok(log.Tail(Math.Clamp(tail ?? 200, 1, 2000))));

        app.MapPost("/sync-now", async (CancellationToken ct) =>
        {
            var outcome = await coordinator.TryRunAsync(SyncTrigger.ManualGui, ct);
            if (!outcome.Started)
            {
                var status = outcome.Error?.Contains("curso", StringComparison.OrdinalIgnoreCase) == true
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status429TooManyRequests;
                return Results.Json(new { started = false, error = outcome.Error }, statusCode: status);
            }
            return Results.Ok(new
            {
                started = true,
                reconciliationOk = outcome.ReconciliationOk,
                pendingBatches = outcome.PendingBatches,
                error = outcome.Error
            });
        });

        app.MapGet("/diagnostics", async (CancellationToken ct) => Results.Ok(await RunDiagnosticsAsync(ct)));

        app.MapGet("/config", () => Results.Ok(new AgentControlConfig(
            config.ApiUrl, config.Linked, config.BranchCode, config.BusinessId, config.InstallationId, config.MachineName)));

        app.MapPost("/link", (LinkDeviceCredential credential) =>
        {
            if (!Guid.TryParse(credential.InstallationId, out _))
                return Results.BadRequest(new { error = "installationId inválido." });
            if (string.IsNullOrWhiteSpace(credential.BranchCode))
                return Results.BadRequest(new { error = "Falta branchCode." });
            if (string.IsNullOrWhiteSpace(credential.Token) || credential.Token.Length < 32)
                return Results.BadRequest(new { error = "El token del dispositivo no es válido." });

            // El servicio ya corre elevado (LocalSystem) y es dueño del ACL sobre el archivo
            // DPAPI en ProgramData — por eso es él, no la GUI (proceso de usuario normal), quien
            // persiste la credencial. La GUI solo la transporta desde central-api hasta aquí.
            config.ApplyLink(
                credential.InstallationId, credential.BranchCode, credential.BusinessId,
                credential.Token, credential.ApiUrl);
            statusStore.Update(s => s with
            {
                Linked = true,
                BranchCode = credential.BranchCode,
                State = AgentOperationalState.Idle,
                LastError = null
            });
            log.Info($"Equipo vinculado a la sucursal {credential.BranchCode} (installationId={credential.InstallationId}).");
            return Results.Ok(new { linked = true, credential.BranchCode, credential.InstallationId });
        });

        app.MapPost("/unlink", () =>
        {
            // "Cerrar sesión" del equipo: borra la identidad de dispositivo local para que la GUI
            // vuelva a exigir login + selección de sucursal antes de poder vincular de nuevo — sin
            // esto, "Vincular / reemplazar equipo…" podía relanzarse aunque ya hubiera un vínculo
            // activo. No revoca nada en central-api (ver ExtractorConfig.ClearLink).
            if (!config.Linked)
                return Results.Ok(new { linked = false, alreadyUnlinked = true });

            var previousBranch = config.BranchCode;
            config.ClearLink();
            statusStore.Update(s => s with
            {
                Linked = false,
                BranchCode = "",
                State = AgentOperationalState.Idle,
                LastError = null
            });
            log.Info($"Equipo desvinculado de la sucursal {previousBranch} (sesión cerrada desde la GUI).");
            return Results.Ok(new { linked = false });
        });

        try
        {
            await app.StartAsync(cancellationToken);
            log.Info($"API de control local escuchando en http://127.0.0.1:{config.ControlPort}.");
        }
        catch (Exception ex)
        {
            // Si el puerto ya está tomado (dos instancias, otra app), el agente sigue
            // funcionando igual: la GUI local simplemente mostrará "servicio no disponible".
            log.Warn($"No se pudo iniciar la API de control local en el puerto {config.ControlPort}: {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (app is not null) await app.StopAsync(cancellationToken);
    }

    private async Task<DiagnosticsReport> RunDiagnosticsAsync(CancellationToken ct)
    {
        var checks = new List<DiagnosticCheck>
        {
            await CheckSqlAsync(ct),
            await CheckApiAsync(ct),
            CheckLocalWrite()
        };
        return new DiagnosticsReport(checks.All(c => c.Ok), checks);
    }

    private async Task<DiagnosticCheck> CheckSqlAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(config.BuildConnectionString());
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            await command.ExecuteScalarAsync(ct);
            return new DiagnosticCheck("SQL Server (RestaurantAgent)", true, $"{config.Server} / {config.Database}");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck("SQL Server (RestaurantAgent)", false, ex.Message);
        }
    }

    private async Task<DiagnosticCheck> CheckApiAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiUrl))
            return new DiagnosticCheck("API central", false, "No configurada (agente en modo local).");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync($"{config.ApiUrl}/api/health/live", ct);
            return new DiagnosticCheck("API central", response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck("API central", false, ex.Message);
        }
    }

    /// <summary>
    /// Prueba de escritura con un archivo TEMPORAL propio, creado y borrado de inmediato.
    /// Nunca toca <c>sync-queue.db</c>, la carpeta de salida real ni ningún archivo productivo.
    /// </summary>
    private DiagnosticCheck CheckLocalWrite()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(config.QueuePath)) ?? ".";
        var probePath = Path.Combine(directory, $".diag-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(probePath, "diagnóstico");
            return new DiagnosticCheck("Carpeta de datos local", true, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DiagnosticCheck("Carpeta de datos local", false, ex.Message);
        }
        finally
        {
            try { if (File.Exists(probePath)) File.Delete(probePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
