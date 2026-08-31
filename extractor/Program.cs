using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoftRestaurant.Extractor;

if (args.Length > 0 && args[0] == "--protect-config")
{
    if (args.Length != 3)
    {
        Console.Error.WriteLine("Uso: --protect-config <entrada.json> <salida.dpapi>");
        return 64;
    }

    try
    {
        ProtectedSettings.ProtectFile(args[1], args[2]);
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine($"No se pudo proteger la configuración: {ex.Message}");
        return 65;
    }
}

if (args.Length > 0 && args[0] == "--config-status")
{
    // Usado por el instalador para decidir, en una actualización, si ya existe una
    // configuración protegida completa en este equipo (conexión SQL + credencial o
    // clave de activación) y por lo tanto no debe volver a pedirla ni sobrescribirla.
    var configStatusPath = args.Length > 1 ? args[1] : null;
    try
    {
        if (!ProtectedSettings.TryLoadValid(configStatusPath, out var reason))
        {
            Console.Error.WriteLine($"Configuración existente no utilizable: {reason}");
            return 1;
        }

        Console.WriteLine("Configuración existente válida y completa.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine($"No se pudo leer la configuración existente: {ex.Message}");
        return 1;
    }
}

if (args.Length > 0 && args[0] == "--import-connector-credential")
{
    if (args.Length != 2)
    {
        Console.Error.WriteLine("Uso: --import-connector-credential <credencial.json>");
        return 64;
    }

    var credentialPath = args[1];
    try
    {
        var credential = JsonSerializer.Deserialize<ConnectorCredentialImport>(
            File.ReadAllText(credentialPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("El archivo de credencial está vacío.");

        if (!Guid.TryParse(credential.ConnectorId, out _))
            throw new ArgumentException("connectorId no es un UUID válido.");
        if (string.IsNullOrWhiteSpace(credential.BranchCode))
            throw new ArgumentException("Falta branchCode.");
        if (string.IsNullOrWhiteSpace(credential.Token) || credential.Token.Length < 32)
            throw new ArgumentException("El token del conector no es válido.");

        ProtectedSettings.CompleteActivation(
            credential.ConnectorId,
            credential.BranchCode,
            credential.Token);
        Console.WriteLine("Credencial del conector importada y protegida con DPAPI.");
        return 0;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
    {
        Console.Error.WriteLine($"No se pudo importar la credencial: {ex.Message}");
        return 65;
    }
    finally
    {
        if (File.Exists(credentialPath)) File.Delete(credentialPath);
    }
}

ExtractorConfig cfg;
try
{
    cfg = ExtractorConfig.Resolve(args);
}
catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
{
    Console.Error.WriteLine($"Configuración inválida: {ex.Message}");
    return 64;
}

Console.WriteLine("SoftRestaurant Sync Agent — solo lectura");
Console.WriteLine($"  SQL: {cfg.Server} / {cfg.Database} ({(cfg.TrustedConnection ? "Windows Auth" : "SQL Login")})");
Console.WriteLine($"  Sucursal: {cfg.BranchCode}");
Console.WriteLine($"  Modo: {(cfg.Watch ? "servicio periódico" : cfg.SendEnabled ? "extracción y envío" : "extracción local")}");

if (cfg.Watch)
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddWindowsService(options => options.ServiceName = "SoftRestaurant Sync Agent");
    builder.Services.AddSingleton(cfg);
    builder.Services.AddSingleton<AgentStatusStore>();
    builder.Services.AddSingleton<AgentLog>();
    builder.Services.AddSingleton<SyncCoordinator>();
    builder.Services.AddHostedService<AgentControlServer>();
    builder.Services.AddHostedService<HeartbeatWorker>();
    builder.Services.AddHostedService<SyncWorker>();
    await builder.Build().RunAsync();
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

try
{
    var result = await new AgentRunService(cfg).RunOnceAsync(cts.Token);
    if (!result.ReconciliationOk) return 1;
    if (result.PendingBatches > 0)
    {
        Console.Error.WriteLine($"Extracción correcta; quedan {result.PendingBatches} lote(s) pendientes en la cola.");
        return 3;
    }

    Console.WriteLine("OK — extracción conciliada y cola sincronizada.");
    return 0;
}
catch (Microsoft.Data.SqlClient.SqlException ex)
{
    Console.Error.WriteLine($"Error de SQL Server: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Cancelado.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

internal sealed record ConnectorCredentialImport(string ConnectorId, string BranchCode, string Token);
