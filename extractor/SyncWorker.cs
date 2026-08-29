using Microsoft.Extensions.Hosting;

namespace SoftRestaurant.Extractor;

internal sealed class SyncWorker(ExtractorConfig config) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"Agente activo. Sucursal={config.BranchCode}, intervalo={config.SyncIntervalSeconds}s.");
        var runner = new AgentRunService(config);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error de ciclo: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(config.SyncIntervalSeconds), stoppingToken);
        }
    }
}
