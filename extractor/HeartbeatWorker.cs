using Microsoft.Extensions.Hosting;

namespace SoftRestaurant.Extractor;

/// <summary>
/// Envía el latido del agente cada <see cref="ExtractorConfig.HeartbeatIntervalSeconds"/> (30–60s),
/// completamente desacoplado del ciclo de sincronización de <see cref="SyncWorker"/>: corre en su
/// propio timer y sigue latiendo aunque no haya sincronización en curso o una esté tardando mucho.
/// Es también quien detecta una solicitud remota de sincronización (<c>syncRequestedAt</c> en la
/// respuesta) y se la pide a <see cref="SyncCoordinator"/>, el único punto de entrada para correr
/// una sincronización.
///
/// Antes de vincular el equipo (<see cref="ExtractorConfig.Linked"/> falso) simplemente espera:
/// no hay identidad de dispositivo con la que autenticarse todavía. En cuanto
/// <c>AgentControlServer</c> recibe <c>POST /link</c> y actualiza <c>config</c> en caliente, el
/// siguiente tick ya crea el cliente con la credencial nueva — no hace falta reiniciar el
/// servicio (ver <see cref="ExtractorConfig.ApplyLink"/>).
/// </summary>
internal sealed class HeartbeatWorker(
    ExtractorConfig config, AgentStatusStore statusStore, SyncCoordinator coordinator, AgentLog log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.SendEnabled) return; // Sin envío configurado (modo local/CLI), no hay a quién latirle.

        HeartbeatClient? client = null;
        var loggedUnlinkedOnce = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!config.Linked)
            {
                if (!loggedUnlinkedOnce)
                {
                    log.Info("Equipo aún no vinculado; esperando a que la GUI complete la vinculación.");
                    loggedUnlinkedOnce = true;
                }
                statusStore.Update(s => s with { Linked = false });
                await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatIntervalSeconds), stoppingToken);
                continue;
            }
            loggedUnlinkedOnce = false;

            try
            {
                client ??= new HeartbeatClient(config.ApiUrl!, config.DeviceToken!, config.InstallationId!);
                await SendOnceAsync(client, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AgentApiException ex) when (ex.IsUnauthorized)
            {
                // Credencial revocada (o el equipo fue reemplazado desde el dashboard): dejar de
                // insistir con este token y avisar con claridad, sin crash-loop. Un nuevo
                // POST /link desde la GUI reactiva el latido en el siguiente tick.
                client = null;
                statusStore.Update(s => s with
                {
                    State = AgentOperationalState.Revoked,
                    ApiConnected = false,
                    LastError = "La credencial de este equipo fue revocada. Vuelve a vincularlo desde el panel del agente."
                });
                log.Warn($"Latido rechazado (credencial revocada): {ex.Message}");
            }
            catch (Exception ex)
            {
                statusStore.Update(s => s with { ApiConnected = false });
                log.Warn($"Latido no enviado: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(config.HeartbeatIntervalSeconds), stoppingToken);
        }
    }

    private async Task SendOnceAsync(HeartbeatClient client, CancellationToken ct)
    {
        var status = statusStore.Current;
        var request = new HeartbeatRequest(
            config.BranchCode,
            status.AgentVersion,
            status.State.ToString(),
            status.LastSuccessAt,
            status.LastError,
            status.PendingBatches,
            status.LastSyncRequestHandledAtUtc);

        var response = await client.SendAsync(request, ct);
        statusStore.Update(s => s with
        {
            Linked = true,
            LastHeartbeatAt = DateTime.UtcNow,
            ApiConnected = true,
            LastSyncRequestedAtUtc = response.SyncRequestedAt,
            State = s.State == AgentOperationalState.Revoked ? AgentOperationalState.Idle : s.State
        });

        if (response.SyncRequestedAt is { } requestedAt &&
            (status.LastSyncRequestHandledAtUtc is null || requestedAt > status.LastSyncRequestHandledAtUtc))
        {
            log.Info($"Sincronización remota solicitada ({requestedAt:o}); se atenderá vía SyncCoordinator.");
            _ = HandleRemoteRequestAsync(requestedAt, ct);
        }
    }

    private async Task HandleRemoteRequestAsync(DateTime requestedAt, CancellationToken ct)
    {
        var outcome = await coordinator.TryRunAsync(SyncTrigger.Remote, ct);
        if (outcome.Started)
        {
            // Se marca como atendida solo si realmente corrió; si ya había una sincronización
            // en curso, el próximo latido volverá a verla como pendiente y se reintentará.
            statusStore.Update(s => s with { LastSyncRequestHandledAtUtc = requestedAt });
        }
    }
}
