using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestaurantAgent.Extractor;

internal sealed record HeartbeatRequest(
    string BranchCode,
    string AgentVersion,
    string State,
    DateTime? LastSuccessAt,
    string? LastError,
    int PendingBatches,
    DateTime? LastSyncRequestHandledAt);

internal sealed record HeartbeatResponse(DateTime? SyncRequestedAt, DateTime ServerTimeUtc);

/// <summary>
/// Envía el latido independiente del agente hacia <c>POST /api/agents/heartbeat</c> en la API
/// central. Reutiliza el mismo esquema de autenticación por dispositivo que <see cref="AgentApiClient"/>
/// (<see cref="AgentApiClient.ApplyAuthentication"/>), pero es un cliente propio porque el
/// latido es un flujo independiente del envío de lotes.
/// </summary>
internal sealed class HeartbeatClient(string apiUrl, string token, string installationId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<HeartbeatResponse> SendAsync(HeartbeatRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{apiUrl.TrimEnd('/')}/api/agents/heartbeat"))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AgentApiClient.ApplyAuthentication(httpRequest, token, installationId);
        using var response = await client.SendAsync(httpRequest, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new AgentApiException(
                response.StatusCode,
                $"API respondió {(int)response.StatusCode} {response.ReasonPhrase}: {body[..Math.Min(body.Length, 500)]}");
        }
        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, ct)
            ?? new HeartbeatResponse(null, DateTime.UtcNow);
    }
}
