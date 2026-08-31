namespace RestaurantAgent.Extractor;

internal enum SyncTrigger { Cycle, ManualGui, Remote }

internal sealed record SyncRunOutcome(bool Started, bool ReconciliationOk, int PendingBatches, string? Error);

/// <summary>
/// Único punto de entrada para ejecutar una sincronización, sin importar quién la pida:
/// el ciclo periódico (<see cref="SyncWorker"/>), el botón "Sincronizar ahora" de la GUI local
/// (<see cref="AgentControlServer"/>), o una solicitud remota recogida por
/// <see cref="HeartbeatWorker"/>. Un <see cref="SemaphoreSlim"/> garantiza que nunca corran dos
/// sincronizaciones a la vez, preservando la reconciliación/idempotencia de
/// <see cref="AgentRunService"/> tal cual ya funciona.
/// </summary>
internal sealed class SyncCoordinator(ExtractorConfig config, AgentStatusStore statusStore, AgentLog log)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly AgentRunService runner = new(config, log);
    private DateTime lastManualRequestUtc = DateTime.MinValue;

    /// <summary>
    /// Intenta correr una sincronización ya mismo. Si ya hay una en curso, no espera: devuelve
    /// <c>Started = false</c> para que el llamador decida (la API de control local responde 409;
    /// el ciclo periódico y el disparo remoto simplemente lo intentan en el próximo tick).
    /// </summary>
    public async Task<SyncRunOutcome> TryRunAsync(SyncTrigger trigger, CancellationToken ct)
    {
        if (trigger == SyncTrigger.ManualGui)
        {
            // Límite de frecuencia para el botón manual: evita que alguien golpee /sync-now en
            // bucle y sature SQL Server. El ciclo periódico y el disparo remoto no lo necesitan
            // porque ya están acotados por SyncIntervalSeconds / la cadencia del heartbeat.
            var elapsed = DateTime.UtcNow - lastManualRequestUtc;
            if (elapsed < TimeSpan.FromSeconds(15))
                return new SyncRunOutcome(false, false, 0, $"Espera {15 - (int)elapsed.TotalSeconds}s antes de volver a pedirlo.");
        }

        if (!await gate.WaitAsync(0, ct))
            return new SyncRunOutcome(false, false, 0, "Ya hay una sincronización en curso.");

        try
        {
            if (trigger == SyncTrigger.ManualGui) lastManualRequestUtc = DateTime.UtcNow;

            statusStore.Update(s => s with { State = AgentOperationalState.Syncing });
            log.Info($"Sincronización iniciada (origen={trigger}).");

            var result = await runner.RunOnceAsync(ct);

            statusStore.Update(s => s with
            {
                State = result.ReconciliationOk ? AgentOperationalState.Idle : AgentOperationalState.Error,
                LastCycleAt = DateTime.UtcNow,
                LastSuccessAt = result.ReconciliationOk ? DateTime.UtcNow : s.LastSuccessAt,
                LastError = result.ReconciliationOk ? null : "La conciliación con RestaurantAgent no coincidió.",
                LastReconciliationOk = result.ReconciliationOk,
                PendingBatches = result.PendingBatches
            });
            log.Info($"Sincronización terminada (origen={trigger}): conciliaciónOk={result.ReconciliationOk}, pendientes={result.PendingBatches}.");
            return new SyncRunOutcome(true, result.ReconciliationOk, result.PendingBatches, null);
        }
        catch (AgentApiException ex) when (ex.IsUnauthorized)
        {
            statusStore.Update(s => s with
            {
                State = AgentOperationalState.Revoked,
                LastCycleAt = DateTime.UtcNow,
                LastError = "La credencial de este equipo fue revocada. Vuelve a vincularlo desde el panel del agente."
            });
            log.Error($"Sincronización rechazada (origen={trigger}): credencial revocada.");
            return new SyncRunOutcome(true, false, statusStore.Current.PendingBatches, "Credencial revocada.");
        }
        catch (Exception ex)
        {
            statusStore.Update(s => s with
            {
                State = AgentOperationalState.Error,
                LastCycleAt = DateTime.UtcNow,
                LastError = ex.Message
            });
            log.Error($"Sincronización falló (origen={trigger}): {ex.Message}");
            return new SyncRunOutcome(true, false, statusStore.Current.PendingBatches, ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public bool IsRunning => gate.CurrentCount == 0;
}
