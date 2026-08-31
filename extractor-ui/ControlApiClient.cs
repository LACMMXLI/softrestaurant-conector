using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantAgent.Extractor.Ui;

public sealed class AgentStatusDto
{
    public string State { get; set; } = "Idle";
    public string BranchCode { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public bool SendEnabled { get; set; }
    public bool Linked { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public bool? ApiConnected { get; set; }
    public DateTime? LastCycleAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public bool? LastReconciliationOk { get; set; }
    public int PendingBatches { get; set; }
    public bool? SqlConnected { get; set; }
    public DateTime? LastSyncRequestedAtUtc { get; set; }
    public DateTime? LastSyncRequestHandledAtUtc { get; set; }
}

public sealed class DiagnosticCheckDto
{
    public string Name { get; set; } = "";
    public bool Ok { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class DiagnosticsReportDto
{
    public bool Ok { get; set; }
    public List<DiagnosticCheckDto> Checks { get; set; } = [];
}

public sealed class SyncNowResultDto
{
    public bool Started { get; set; }
    public bool? ReconciliationOk { get; set; }
    public int? PendingBatches { get; set; }
    public string? Error { get; set; }
}

public sealed class AgentControlConfigDto
{
    public string? ApiUrl { get; set; }
    public bool Linked { get; set; }
    public string BranchCode { get; set; } = "";
    public string? BusinessId { get; set; }
    public string? InstallationId { get; set; }
    public string MachineName { get; set; } = "";
}

public sealed class LinkDeviceCredentialDto
{
    public string InstallationId { get; set; } = "";
    public string BranchCode { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public string Token { get; set; } = "";
    public string? ApiUrl { get; set; }
}

/// <summary>
/// Cliente de la API de control local del servicio (<c>127.0.0.1:&lt;puerto&gt;</c>, sin
/// autenticación: ver <c>AgentControlServer</c> en el proyecto del agente). Si el servicio no
/// está corriendo o no tiene la API de control activa, las llamadas fallan con
/// <see cref="HttpRequestException"/> y la GUI lo interpreta como "servicio no disponible".
/// </summary>
public sealed class ControlApiClient(int port)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(), new FlexibleStringJsonConverter() }
    };

    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(5)
    };

    public Task<AgentStatusDto?> GetStatusAsync(CancellationToken ct) =>
        client.GetFromJsonAsync<AgentStatusDto>("/status", JsonOptions, ct);

    public Task<List<string>?> GetLogsAsync(int tail, CancellationToken ct) =>
        client.GetFromJsonAsync<List<string>>($"/logs?tail={tail}", JsonOptions, ct);

    public async Task<DiagnosticsReportDto?> GetDiagnosticsAsync(CancellationToken ct)
    {
        using var response = await client.GetAsync("/diagnostics", ct);
        return await response.Content.ReadFromJsonAsync<DiagnosticsReportDto>(JsonOptions, ct);
    }

    public async Task<SyncNowResultDto> RequestSyncNowAsync(CancellationToken ct)
    {
        using var response = await client.PostAsync("/sync-now", content: null, ct);
        var result = await response.Content.ReadFromJsonAsync<SyncNowResultDto>(JsonOptions, ct);
        return result ?? new SyncNowResultDto { Started = false, Error = "Respuesta vacía del agente." };
    }

    public Task<AgentControlConfigDto?> GetConfigAsync(CancellationToken ct) =>
        client.GetFromJsonAsync<AgentControlConfigDto>("/config", JsonOptions, ct);

    /// <summary>Entrega al servicio la credencial de dispositivo obtenida de central-api para que la persista vía DPAPI (ver AgentControlServer.POST /link).</summary>
    public async Task<bool> LinkAsync(LinkDeviceCredentialDto credential, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/link", credential, JsonOptions, ct);
        return response.IsSuccessStatusCode;
    }
}
