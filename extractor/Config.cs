using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace RestaurantAgent.Extractor;

/// <summary>
/// Configuración de conexión y extracción, resuelta con esta prioridad:
/// argumentos de línea de comandos > variables de entorno > archivo protegido DPAPI > appsettings.json > valores por defecto.
///
/// La identidad de dispositivo (InstallationId/DeviceToken/BusinessId) es distinta del resto:
/// no se resuelve una sola vez al arrancar, puede llegar DESPUÉS de que el servicio ya esté
/// corriendo (vía <see cref="ApplyLink"/>, llamado por AgentControlServer cuando la GUI
/// vincula el equipo) — por eso vive en propiedades mutables (set privado), no en el patrón
/// `required`/`init` del resto de la configuración. Ver extractor/AgentControlServer.cs::/link.
/// </summary>
internal sealed class ExtractorConfig
{
    public required string Server { get; init; }
    public required string Database { get; init; }
    public bool TrustedConnection { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public bool TrustServerCertificate { get; init; } = true;
    public bool Encrypt { get; init; }
    public int ConnectTimeoutSeconds { get; init; } = 15;

    public string OutputDirectory { get; init; } = "./out";
    public string QueuePath { get; init; } = "./data/sync-queue.db";
    public string BranchCode { get; private set; } = "";
    public string? ApiUrl { get; private set; }
    public string? DeviceToken { get; private set; }
    public string? InstallationId { get; private set; }
    public string? BusinessId { get; private set; }
    public string MachineName { get; init; } = Environment.MachineName;
    public bool SendEnabled { get; init; }
    public bool Watch { get; init; }
    public int SyncIntervalSeconds { get; init; } = 60;
    public int HeartbeatIntervalSeconds { get; init; } = 45;
    public int ControlPort { get; init; } = 47811;
    public int RollingDays { get; init; } = 3;
    public bool HasExplicitRange { get; init; }
    public DateTime Desde { get; init; }
    public DateTime Hasta { get; init; } // exclusivo (rango semiabierto), ya con +1 día aplicado si se pasó una fecha simple

    /// <summary>Hay una identidad de dispositivo utilizable. Falso justo después de instalar, hasta que la GUI vincula el equipo.</summary>
    public bool Linked =>
        !string.IsNullOrWhiteSpace(DeviceToken) &&
        !string.IsNullOrWhiteSpace(InstallationId) &&
        !string.IsNullOrWhiteSpace(BranchCode);

    public string BuildConnectionString()
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = Server,
            InitialCatalog = Database,
            TrustServerCertificate = TrustServerCertificate,
            Encrypt = Encrypt,
            ConnectTimeout = ConnectTimeoutSeconds,
            ApplicationName = "RestaurantAgent.Extractor"
        };

        if (TrustedConnection || string.IsNullOrWhiteSpace(User))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = User;
            builder.Password = Password ?? string.Empty;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Aplica una identidad de dispositivo nueva EN VIVO (sin reiniciar el servicio) y la
    /// persiste en el archivo DPAPI. Llamado por AgentControlServer al recibir la credencial
    /// que la GUI obtuvo de central-api tras "Vincular este equipo" — nunca por el propio
    /// agente contactando a la API con una clave de activación (ese flujo ya no existe).
    /// </summary>
    public void ApplyLink(string installationId, string branchCode, string businessId, string token, string? apiUrl)
    {
        ProtectedSettings.ApplyLink(installationId, branchCode, businessId, token, apiUrl);
        InstallationId = installationId;
        BranchCode = branchCode;
        BusinessId = businessId;
        DeviceToken = token;
        if (!string.IsNullOrWhiteSpace(apiUrl)) ApiUrl = apiUrl.TrimEnd('/');
    }

    /// <summary>
    /// Borra la identidad de dispositivo EN VIVO (equivalente a "cerrar sesión" del equipo) y la
    /// quita del archivo DPAPI, preservando la conexión SQL y la URL de la API. Tras esto
    /// <see cref="Linked"/> vuelve a ser falso, igual que justo después de instalar — el usuario
    /// debe iniciar sesión y vincular de nuevo para reanudar la sincronización. No revoca nada en
    /// central-api: el siguiente intento de vinculación detectará el conector todavía activo y
    /// ofrecerá "reemplazar equipo" (revoca + emite uno nuevo, atómico).
    /// </summary>
    public void ClearLink()
    {
        ProtectedSettings.ClearLink();
        InstallationId = null;
        BranchCode = "";
        BusinessId = null;
        DeviceToken = null;
    }

    public static ExtractorConfig Resolve(string[] args)
    {
        // 1) base: appsettings.json (si existe junto al ejecutable o al proyecto)
        string server = "CARDONA\\SQLEXPRESS";
        string database = "restaurant11";
        bool trusted = true;
        string? user = null;
        string? password = null;
        bool trustCert = true;
        bool encrypt = false;
        int connectTimeout = 15;
        string outDir = "./out";
        string queuePath = "./data/sync-queue.db";
        string branchCode = "";
        string? apiUrl = null;
        string? deviceToken = null;
        string? installationId = null;
        string? businessId = null;
        string machineName = Environment.MachineName;
        int syncIntervalSeconds = 60;
        int heartbeatIntervalSeconds = 45;
        int controlPort = 47811;
        int rollingDays = 3;

        var settingsPath = FindAppSettings();
        if (settingsPath is not null)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.TryGetProperty("connection", out var conn))
            {
                server = conn.TryGetProperty("server", out var s) ? s.GetString() ?? server : server;
                database = conn.TryGetProperty("database", out var d) ? d.GetString() ?? database : database;
                trusted = conn.TryGetProperty("trustedConnection", out var tc) ? tc.GetBoolean() : trusted;
                user = conn.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : user;
                password = conn.TryGetProperty("password", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : password;
                trustCert = conn.TryGetProperty("trustServerCertificate", out var tsc) ? tsc.GetBoolean() : trustCert;
                encrypt = conn.TryGetProperty("encrypt", out var enc) ? enc.GetBoolean() : encrypt;
                connectTimeout = conn.TryGetProperty("connectTimeoutSeconds", out var ct) ? ct.GetInt32() : connectTimeout;
            }
            if (doc.RootElement.TryGetProperty("extraction", out var ext))
            {
                outDir = ext.TryGetProperty("outputDirectory", out var od) ? od.GetString() ?? outDir : outDir;
            }
            if (doc.RootElement.TryGetProperty("sync", out var sync))
            {
                queuePath = sync.TryGetProperty("queuePath", out var qp) ? qp.GetString() ?? queuePath : queuePath;
                branchCode = sync.TryGetProperty("branchCode", out var bc) ? bc.GetString() ?? branchCode : branchCode;
                apiUrl = sync.TryGetProperty("apiUrl", out var au) && au.ValueKind == JsonValueKind.String ? au.GetString() : apiUrl;
                syncIntervalSeconds = sync.TryGetProperty("intervalSeconds", out var si) ? si.GetInt32() : syncIntervalSeconds;
                rollingDays = sync.TryGetProperty("rollingDays", out var rd) ? rd.GetInt32() : rollingDays;
            }
        }

        // 2) configuración cifrada por máquina (instalador de Windows + vinculación en vivo)
        var protectedSettings = ProtectedSettings.Load();
        server = GetProtectedRequired("SRX_SQL_SERVER", server);
        database = GetProtectedRequired("SRX_SQL_DATABASE", database);
        user = GetProtectedOptional("SRX_SQL_USER", user);
        password = GetProtectedOptional("SRX_SQL_PASSWORD", password);
        apiUrl = GetProtectedOptional("SRX_API_URL", apiUrl);
        deviceToken = GetProtectedOptional("SRX_DEVICE_TOKEN", deviceToken);
        installationId = GetProtectedOptional("SRX_INSTALLATION_ID", installationId);
        businessId = GetProtectedOptional("SRX_BUSINESS_ID", businessId);
        machineName = GetProtectedRequired("SRX_MACHINE_NAME", machineName);
        branchCode = GetProtectedOptional("SRX_BRANCH_CODE", branchCode) ?? branchCode;
        queuePath = GetProtectedRequired("SRX_QUEUE_PATH", queuePath);
        outDir = GetProtectedRequired("SRX_OUTPUT_PATH", outDir);
        if (!string.IsNullOrWhiteSpace(user)) trusted = false;

        // 3) variables de entorno
        server = Environment.GetEnvironmentVariable("SRX_SQL_SERVER") ?? server;
        database = Environment.GetEnvironmentVariable("SRX_SQL_DATABASE") ?? database;
        user = Environment.GetEnvironmentVariable("SRX_SQL_USER") ?? user;
        password = Environment.GetEnvironmentVariable("SRX_SQL_PASSWORD") ?? password;
        apiUrl = Environment.GetEnvironmentVariable("SRX_API_URL") ?? apiUrl;
        deviceToken = Environment.GetEnvironmentVariable("SRX_DEVICE_TOKEN") ?? deviceToken;
        installationId = Environment.GetEnvironmentVariable("SRX_INSTALLATION_ID") ?? installationId;
        businessId = Environment.GetEnvironmentVariable("SRX_BUSINESS_ID") ?? businessId;
        machineName = Environment.GetEnvironmentVariable("SRX_MACHINE_NAME") ?? machineName;
        branchCode = Environment.GetEnvironmentVariable("SRX_BRANCH_CODE") ?? branchCode;
        queuePath = Environment.GetEnvironmentVariable("SRX_QUEUE_PATH") ?? queuePath;
        outDir = Environment.GetEnvironmentVariable("SRX_OUTPUT_PATH") ?? outDir;
        if (!string.IsNullOrWhiteSpace(user)) trusted = false;
        if (int.TryParse(Environment.GetEnvironmentVariable("SRX_HEARTBEAT_SECONDS"), out var envHeartbeat))
            heartbeatIntervalSeconds = envHeartbeat;
        if (int.TryParse(Environment.GetEnvironmentVariable("SRX_CONTROL_PORT"), out var envControlPort))
            controlPort = envControlPort;

        // 4) argumentos de línea de comandos
        DateTime? desdeArg = null, hastaArg = null;
        bool sendEnabled = false;
        bool watch = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--server" when i + 1 < args.Length: server = args[++i]; break;
                case "--database" when i + 1 < args.Length: database = args[++i]; break;
                case "--user" when i + 1 < args.Length: user = args[++i]; trusted = false; break;
                case "--password" when i + 1 < args.Length: password = args[++i]; trusted = false; break;
                case "--trusted": trusted = true; break;
                case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                case "--desde" when i + 1 < args.Length: desdeArg = DateTime.Parse(args[++i]); break;
                case "--hasta" when i + 1 < args.Length: hastaArg = DateTime.Parse(args[++i]); break;
                case "--send": sendEnabled = true; break;
                case "--watch": watch = true; sendEnabled = true; break;
                case "--api-url" when i + 1 < args.Length: apiUrl = args[++i]; break;
                case "--device-token" when i + 1 < args.Length: deviceToken = args[++i]; break;
                case "--installation-id" when i + 1 < args.Length: installationId = args[++i]; break;
                case "--machine-name" when i + 1 < args.Length: machineName = args[++i]; break;
                case "--branch" when i + 1 < args.Length: branchCode = args[++i]; break;
                case "--queue" when i + 1 < args.Length: queuePath = args[++i]; break;
                case "--interval" when i + 1 < args.Length: syncIntervalSeconds = int.Parse(args[++i]); break;
                case "--rolling-days" when i + 1 < args.Length: rollingDays = int.Parse(args[++i]); break;
            }
        }

        if (watch)
        {
            desdeArg = null;
            hastaArg = null;
        }

        // A diferencia del esquema anterior (activación por clave), un servicio recién
        // instalado arranca legítimamente SIN identidad de dispositivo — se vincula después,
        // desde la GUI. Por eso "--send"/"--watch" solo exigen una URL de API configurada, no
        // una credencial: SyncWorker/HeartbeatWorker esperan en estado "no vinculado" (ver
        // ExtractorConfig.Linked) hasta que AgentControlServer reciba POST /link.
        if (sendEnabled && string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new ArgumentException("Para enviar se requiere SRX_API_URL.");
        }
        if (!string.IsNullOrWhiteSpace(installationId) && !Guid.TryParse(installationId, out _))
            throw new ArgumentException("SRX_INSTALLATION_ID no es un UUID válido.");
        if (string.IsNullOrWhiteSpace(machineName) || machineName.Length > 200)
            throw new ArgumentException("SRX_MACHINE_NAME debe tener entre 1 y 200 caracteres.");

        syncIntervalSeconds = Math.Max(15, syncIntervalSeconds);
        heartbeatIntervalSeconds = Math.Clamp(heartbeatIntervalSeconds, 30, 60);
        controlPort = controlPort is > 0 and <= 65535 ? controlPort : 47811;
        rollingDays = Math.Clamp(rollingDays, 1, 30);

        // Rango por defecto: ayer a hoy (semiabierto), igual al ciclo real de sincronización.
        var hoy = DateTime.Today;
        var desde = (desdeArg ?? hoy.AddDays(-1)).Date;
        var hasta = (hastaArg ?? hoy).Date.AddDays(1); // +1 para hacerlo exclusivo si el usuario dio una fecha "hasta" inclusiva

        var resolved = new ExtractorConfig
        {
            Server = server,
            Database = database,
            TrustedConnection = trusted,
            User = user,
            Password = password,
            TrustServerCertificate = trustCert,
            Encrypt = encrypt,
            ConnectTimeoutSeconds = connectTimeout,
            OutputDirectory = outDir,
            QueuePath = queuePath,
            MachineName = machineName,
            SendEnabled = sendEnabled,
            Watch = watch,
            SyncIntervalSeconds = syncIntervalSeconds,
            HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
            ControlPort = controlPort,
            RollingDays = rollingDays,
            HasExplicitRange = desdeArg.HasValue || hastaArg.HasValue,
            Desde = desde,
            Hasta = hasta
        };
        resolved.BranchCode = branchCode;
        resolved.ApiUrl = apiUrl?.TrimEnd('/');
        resolved.DeviceToken = deviceToken;
        resolved.InstallationId = installationId;
        resolved.BusinessId = businessId;
        return resolved;

        string GetProtectedRequired(string key, string current) =>
            protectedSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : current;

        string? GetProtectedOptional(string key, string? current) =>
            protectedSettings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : current;
    }

    public (DateTime Desde, DateTime Hasta) GetRunRange()
    {
        if (!Watch || HasExplicitRange)
        {
            return (Desde, Hasta);
        }

        var today = DateTime.Today;
        return (today.AddDays(-(RollingDays - 1)), today.AddDays(1));
    }

    private static string? FindAppSettings()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}

internal static class ProtectedSettings
{
    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        Converters = { new FlexibleStringDictionaryConverter() }
    };

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RestaurantAgentSyncAgent",
        "agent-settings.dpapi");

    public static void ProtectFile(string inputPath, string outputPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI requiere Windows.");

        var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(inputPath, Encoding.UTF8), SettingsJsonOptions)
            ?? throw new ArgumentException("El archivo de configuración está vacío.");

        // El instalador ya NO recoge ninguna credencial de dispositivo (ni clave de
        // activación, ni token): eso ocurre después, cuando la GUI vincula el equipo desde una
        // sesión de usuario. Aquí solo se protegen los datos de conexión SQL + la URL de la API.
        string[] required =
        [
            "SRX_API_URL",
            "SRX_SQL_SERVER", "SRX_SQL_DATABASE", "SRX_SQL_USER", "SRX_SQL_PASSWORD"
        ];
        if (required.Any(key => !settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Faltan valores requeridos en la configuración.");

        WriteProtected(outputPath, settings);
    }

    /// <summary>Persiste la identidad de dispositivo emitida por central-api, agregándola al archivo protegido existente (no lo reemplaza: preserva SQL y API URL).</summary>
    public static void ApplyLink(string installationId, string branchCode, string businessId, string token, string? apiUrl)
    {
        var path = GetPath();
        if (!File.Exists(path))
            throw new InvalidOperationException("No existe el archivo protegido donde guardar la credencial.");

        var settings = Load().ToDictionary(x => x.Key, x => x.Value);
        settings["SRX_INSTALLATION_ID"] = installationId;
        settings["SRX_BRANCH_CODE"] = branchCode;
        settings["SRX_BUSINESS_ID"] = businessId;
        settings["SRX_DEVICE_TOKEN"] = token;
        if (!string.IsNullOrWhiteSpace(apiUrl)) settings["SRX_API_URL"] = apiUrl;
        WriteProtected(path, settings);
    }

    /// <summary>Quita del archivo protegido las cuatro claves de identidad de dispositivo, dejando intactas SQL y la URL de la API.</summary>
    public static void ClearLink()
    {
        var path = GetPath();
        if (!File.Exists(path)) return;

        var settings = Load().ToDictionary(x => x.Key, x => x.Value);
        settings.Remove("SRX_INSTALLATION_ID");
        settings.Remove("SRX_BRANCH_CODE");
        settings.Remove("SRX_BUSINESS_ID");
        settings.Remove("SRX_DEVICE_TOKEN");
        WriteProtected(path, settings);
    }

    private static void WriteProtected(string outputPath, Dictionary<string, string> settings)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI requiere Windows.");

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(settings);
        var tempPath = outputPath + ".new";
        try
        {
            var protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.LocalMachine);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.WriteAllBytes(tempPath, protectedBytes);
            File.Move(tempPath, outputPath, true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public static IReadOnlyDictionary<string, string> Load()
    {
        var path = GetPath();
        if (!File.Exists(path)) return new Dictionary<string, string>();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI requiere Windows.");

        var plaintext = ProtectedData.Unprotect(
            File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, SettingsJsonOptions)
                ?? throw new JsonException("La configuración protegida está vacía.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// Comprueba, sin lanzar excepciones, si ya existe en este equipo una configuración SQL
    /// protegida completa. La usa el instalador para decidir, en una actualización, si puede
    /// saltarse las páginas de conexión SQL. Deliberadamente NO exige identidad de dispositivo:
    /// vincular el equipo es un paso posterior a la instalación, no algo que el instalador deba
    /// verificar — un vínculo existente sobrevive a una actualización sin que el instalador
    /// necesite saber de él (nunca escribe esas claves).
    /// </summary>
    public static bool TryLoadValid(string? path, out string reason)
    {
        reason = "";
        var actualPath = string.IsNullOrWhiteSpace(path) ? GetPath() : path;

        if (!File.Exists(actualPath))
        {
            reason = "no existe el archivo de configuración protegida";
            return false;
        }
        if (!OperatingSystem.IsWindows())
        {
            reason = "DPAPI requiere Windows";
            return false;
        }

        Dictionary<string, string>? settings;
        try
        {
            var protectedBytes = File.ReadAllBytes(actualPath);
            var plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            try
            {
                settings = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext, SettingsJsonOptions);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (CryptographicException)
        {
            reason = "no se pudo descifrar (¿archivo de otra máquina o usuario?)";
            return false;
        }
        catch (JsonException)
        {
            reason = "el contenido descifrado no es JSON válido";
            return false;
        }

        if (settings is null)
        {
            reason = "la configuración protegida está vacía";
            return false;
        }

        bool Has(string key) => settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

        if (!Has("SRX_SQL_SERVER") || !Has("SRX_SQL_DATABASE"))
        {
            reason = "faltan datos de conexión SQL";
            return false;
        }

        return true;
    }

    private static string GetPath() =>
        Environment.GetEnvironmentVariable("SRX_PROTECTED_CONFIG") ?? DefaultPath;
}

/// <summary>
/// Acepta configuraciones antiguas donde algunos valores escalares fueron guardados como
/// números o booleanos, aunque el modelo interno los trate como texto.
/// </summary>
internal sealed class FlexibleStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("La configuración debe ser un objeto JSON.");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => throw new JsonException($"El valor de '{property.Name}' debe ser escalar.")
            };
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var pair in value)
            writer.WriteString(pair.Key, pair.Value);
        writer.WriteEndObject();
    }
}
