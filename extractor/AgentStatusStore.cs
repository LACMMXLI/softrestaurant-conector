using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantAgent.Extractor;

internal enum AgentOperationalState { Idle, Syncing, Error, Revoked }

/// <summary>
/// Foto del estado del agente que consume la API de control local (<see cref="AgentControlServer"/>)
/// y, por lo tanto, la GUI de bandeja. Mantiene tres conceptos separados a propósito:
/// <see cref="LastHeartbeatAt"/> (¿el agente está vivo?, lo actualiza únicamente
/// <see cref="HeartbeatWorker"/>), <see cref="LastSuccessAt"/> (última sincronización
/// completada, la actualiza únicamente <see cref="SyncCoordinator"/>) y <see cref="State"/>
/// (qué está haciendo ahora mismo). No mezclar estas tres señales entre sí.
/// </summary>
internal sealed record AgentStatus
{
    public AgentOperationalState State { get; init; } = AgentOperationalState.Idle;
    public string BranchCode { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string AgentVersion { get; init; } = "";
    public bool SendEnabled { get; init; }

    /// <summary>Hay identidad de dispositivo (ver ExtractorConfig.Linked). Falso justo tras instalar; la GUI debe ofrecer vincular en ese caso.</summary>
    public bool Linked { get; init; }

    /// <summary>Último latido enviado con éxito al backend. Independiente del ciclo de sync.</summary>
    public DateTime? LastHeartbeatAt { get; init; }
    public bool? ApiConnected { get; init; }

    /// <summary>Resultado de la última corrida de <see cref="SyncCoordinator"/>.</summary>
    public DateTime? LastCycleAt { get; init; }
    public DateTime? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public bool? LastReconciliationOk { get; init; }
    public int PendingBatches { get; init; }
    public bool? SqlConnected { get; init; }

    public DateTime? LastSyncRequestedAtUtc { get; init; }
    public DateTime? LastSyncRequestHandledAtUtc { get; init; }
}

/// <summary>
/// Contenedor thread-safe del <see cref="AgentStatus"/> actual, con persistencia best-effort a
/// disco para que la GUI muestre algo razonable justo tras un reinicio del servicio, antes de
/// que corra el primer ciclo o el primer latido. La persistencia nunca debe tumbar el agente:
/// cualquier error de IO se ignora.
/// </summary>
internal sealed class AgentStatusStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object gate = new();
    private readonly string? persistPath;
    private AgentStatus current;

    public AgentStatusStore(ExtractorConfig config)
    {
        current = new AgentStatus
        {
            BranchCode = config.BranchCode,
            MachineName = config.MachineName,
            AgentVersion = typeof(AgentStatusStore).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            SendEnabled = config.SendEnabled,
            Linked = config.Linked
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(config.QueuePath));
        persistPath = string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, "agent-status.json");
        TryLoad();
    }

    public AgentStatus Current
    {
        get { lock (gate) return current; }
    }

    public void Update(Func<AgentStatus, AgentStatus> mutate)
    {
        AgentStatus updated;
        lock (gate)
        {
            current = mutate(current);
            updated = current;
        }
        TrySave(updated);
    }

    private void TryLoad()
    {
        if (persistPath is null || !File.Exists(persistPath)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<AgentStatus>(File.ReadAllText(persistPath), JsonOptions);
            if (loaded is not null)
            {
                // Al reiniciar, el estado operativo siempre vuelve a Idle: una corrida "Syncing"
                // guardada en disco quedó interrumpida por el reinicio, no sigue en curso.
                // Linked se recalcula del DPAPI actual (fuente de verdad), no del snapshot en
                // disco: puede haber cambiado entre reinicios (vinculado, revocado, reemplazado).
                lock (gate) current = loaded with { State = AgentOperationalState.Idle, Linked = current.Linked };
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Estado persistido corrupto o inaccesible: se ignora y se arranca desde cero.
        }
    }

    private void TrySave(AgentStatus value)
    {
        if (persistPath is null) return;
        try
        {
            var directory = Path.GetDirectoryName(persistPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var tempPath = persistPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(tempPath, persistPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Persistir el estado es una comodidad para la GUI, no algo crítico: nunca debe
            // tumbar al agente si ProgramData está bloqueado momentáneamente (antivirus, etc.).
        }
    }
}
