namespace SoftRestaurant.Extractor;

/// <summary>
/// Log de archivo simple para el agente en modo servicio: cuando corre como servicio de
/// Windows la salida de <see cref="Console"/> no la ve nadie, así que la GUI de bandeja
/// necesita un registro persistente propio. Rotación diaria, retención corta; no reemplaza
/// las líneas de <see cref="Console"/> existentes, solo las espeja a disco en los puntos clave.
/// </summary>
internal sealed class AgentLog
{
    private const int RetentionDays = 14;
    private readonly object gate = new();
    private readonly string directory;

    public AgentLog(ExtractorConfig config)
    {
        var queueDirectory = Path.GetDirectoryName(Path.GetFullPath(config.QueuePath));
        directory = Path.Combine(queueDirectory ?? ".", "logs");
        try
        {
            Directory.CreateDirectory(directory);
            CleanOldFiles();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Si no se puede crear/limpiar la carpeta de logs, las escrituras siguientes
            // simplemente fallarán en silencio (ver Write) en vez de tumbar el agente.
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    /// <summary>Últimas <paramref name="lines"/> líneas del log del día actual (para la GUI/API de control).</summary>
    public IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            var path = CurrentFilePath();
            if (!File.Exists(path)) return [];
            lock (gate)
            {
                var all = File.ReadAllLines(path);
                return all.Length <= lines ? all : all[^lines..];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (gate)
            {
                File.AppendAllLines(CurrentFilePath(), [line]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // El logging a archivo nunca debe interrumpir el flujo de sincronización.
        }
    }

    private string CurrentFilePath() => Path.Combine(directory, $"agent-{DateTime.Now:yyyyMMdd}.log");

    private void CleanOldFiles()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, "agent-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Un archivo bloqueado no debe impedir seguir limpiando/arrancando.
            }
        }
    }
}
