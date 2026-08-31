using Microsoft.Extensions.Hosting;

namespace SoftRestaurant.Extractor;

/// <summary>
/// Ciclo periódico de sincronización. Ya no ejecuta la corrida directamente: se la pide a
/// <see cref="SyncCoordinator"/> (único punto de entrada), que también atiende el botón manual
/// de la GUI y las solicitudes remotas, evitando que corran dos sincronizaciones a la vez.
/// El envío del latido es responsabilidad exclusiva de <see cref="HeartbeatWorker"/>, que corre
/// en paralelo e independiente de este ciclo.
/// </summary>
internal sealed class SyncWorker(ExtractorConfig config, SyncCoordinator coordinator, AgentLog log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"Agente activo. Sucursal={config.BranchCode}, intervalo={config.SyncIntervalSeconds}s.");
        log.Info($"Agente activo. Sucursal={config.BranchCode}, intervalo={config.SyncIntervalSeconds}s.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await coordinator.TryRunAsync(SyncTrigger.Cycle, stoppingToken);
                if (!outcome.Started)
                    Console.Error.WriteLine($"Ciclo omitido: {outcome.Error}");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error de ciclo: {ex.Message}");
                log.Error($"Error de ciclo: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(config.SyncIntervalSeconds), stoppingToken);
        }
    }
}
