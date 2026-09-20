namespace RestaurantAgent.Extractor;

internal sealed record AgentRunResult(bool ReconciliationOk, int PendingBatches);

internal sealed class AgentRunService(ExtractorConfig config, AgentLog? log = null)
{
    public async Task<AgentRunResult> RunOnceAsync(CancellationToken ct)
    {
        SyncOutbox? outbox = null;
        AgentApiClient? client = null;

        // Ya no hay auto-activación por clave: si el equipo aún no está vinculado (ver
        // ExtractorConfig.Linked), este ciclo extrae y concilia igual, pero no intenta enviar
        // nada — se queda en cola local hasta que la GUI vincule el equipo.
        if (config.SendEnabled && config.Linked)
        {
            outbox = new SyncOutbox(config.QueuePath);
            await outbox.InitializeAsync(ct);
            client = new AgentApiClient(config.ApiUrl!, config.DeviceToken!, config.InstallationId!);
            await FlushAsync(outbox, client, ct);
        }

        // La ventana móvil conserva solapamiento para correcciones recientes. El punto de
        // control en SQLite cubre además cualquier periodo en que el servicio estuvo apagado:
        // así el plan comercial nunca interviene en qué cierres se capturan o se conservan.
        var recoveryDesde = outbox is null ? null : await outbox.GetLastExtractedUntilAsync(ct);
        var result = await ExtractionJob.RunAsync(config, ct, recoveryDesde);
        if (outbox is not null && client is not null)
        {
            if (!result.Reconciliation.Ok)
            {
                Console.Error.WriteLine("El lote no se enviará porque la reconciliación falló.");
                log?.Warn("El lote no se enviará porque la reconciliación falló.");
            }
            else
            {
                var batch = ExtractionJob.CreateBatch(config, result);
                await outbox.EnqueueAsync(batch, ct);
                // Solo se avanza después de guardar el lote durablemente. Si se corta la luz
                // antes de este punto, se reextrae el intervalo; la ingesta es idempotente.
                await outbox.SetLastExtractedUntilAsync(result.Hasta, ct);
                Console.WriteLine($"Lote {batch.BatchId} guardado en la cola local.");
                log?.Info($"Lote {batch.BatchId} guardado en la cola local.");
                await FlushAsync(outbox, client, ct);
            }
        }

        return new AgentRunResult(result.Reconciliation.Ok, outbox is null ? 0 : await outbox.CountAsync(ct));
    }

    /// <summary>
    /// Extrae un intervalo histórico explícito sin tocar el checkpoint del ciclo diario. El
    /// lote viaja por la misma cola SQLite e ingesta idempotente que el ciclo normal.
    /// </summary>
    public async Task<AgentRunResult> RunHistoricalAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!config.SendEnabled || !config.Linked)
            throw new InvalidOperationException("El agente debe estar vinculado para ejecutar un backfill histórico.");

        var outbox = new SyncOutbox(config.QueuePath);
        await outbox.InitializeAsync(ct);
        var client = new AgentApiClient(config.ApiUrl!, config.DeviceToken!, config.InstallationId!);
        await FlushAsync(outbox, client, ct);

        var result = await ExtractionJob.RunAsync(
            config, ct,
            forcedDesde: from.ToDateTime(TimeOnly.MinValue),
            forcedHasta: to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!result.Reconciliation.Ok)
        {
            log?.Warn($"Backfill histórico [{from:yyyy-MM-dd}, {to:yyyy-MM-dd}] no enviado: conciliación fallida.");
            return new AgentRunResult(false, await outbox.CountAsync(ct));
        }

        var batch = ExtractionJob.CreateBatch(config, result);
        await outbox.EnqueueAsync(batch, ct);
        log?.Info($"Backfill histórico [{from:yyyy-MM-dd}, {to:yyyy-MM-dd}] encolado como {batch.BatchId}.");
        await FlushAsync(outbox, client, ct);
        return new AgentRunResult(true, await outbox.CountAsync(ct));
    }

    private async Task FlushAsync(SyncOutbox outbox, AgentApiClient client, CancellationToken ct)
    {
        for (var sent = 0; sent < 20; sent++)
        {
            var pending = await outbox.GetNextAsync(ct);
            if (pending is null) return;
            try
            {
                await client.SendAsync(pending.Batch, ct);
                await outbox.MarkSentAsync(pending.Id, ct);
                Console.WriteLine($"Lote {pending.Id} confirmado por la API.");
                log?.Info($"Lote {pending.Id} confirmado por la API.");
            }
            catch (AgentApiException ex) when (ex.IsUnauthorized)
            {
                // Credencial revocada o rechazada: no tiene sentido seguir reintentando este
                // lote (ni los siguientes) hasta que alguien vincule el equipo de nuevo desde la
                // GUI. Se deja en cola (no se pierde) y se propaga para que SyncCoordinator
                // marque el estado como Revoked en vez de un simple error transitorio.
                await outbox.MarkFailedAsync(pending, ex.Message, ct);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or AgentApiException)
            {
                await outbox.MarkFailedAsync(pending, ex.Message, ct);
                Console.Error.WriteLine($"API no disponible; el lote queda en cola: {ex.Message}");
                log?.Warn($"API no disponible; el lote queda en cola: {ex.Message}");
                return;
            }
        }
    }
}
