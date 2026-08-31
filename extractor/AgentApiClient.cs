using System.Net.Http.Json;
using System.Net.Http.Headers;
using RestaurantAgent.Sync.Contracts;

namespace RestaurantAgent.Extractor;

internal sealed class AgentApiClient(string apiUrl, string token, string installationId)
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(3) };

    public async Task SendAsync(SyncBatch batch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{apiUrl.TrimEnd('/')}/api/ingestion/batches"))
        {
            Content = JsonContent.Create(batch, options: ExtractionJob.JsonOptions)
        };
        ApplyAuthentication(request, token, installationId);
        using var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new AgentApiException(
            response.StatusCode,
            $"API respondió {(int)response.StatusCode} {response.ReasonPhrase}: {body[..Math.Min(body.Length, 1000)]}");
    }

    /// <summary>Identidad de dispositivo únicamente — ya no existe el modo legacy de token compartido por sucursal.</summary>
    internal static void ApplyAuthentication(
        HttpRequestMessage request, string token, string installationId)
    {
        request.Headers.Add("X-Connector-Id", installationId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

/// <summary>
/// Error HTTP de la API central que conserva el código de estado, para que quien lo capture
/// pueda distinguir "credencial revocada/rechazada" (401/403 — el agente debe pasar a estado
/// Revoked y dejar de reintentar) de cualquier otro fallo transitorio.
/// </summary>
internal sealed class AgentApiException(System.Net.HttpStatusCode statusCode, string message) : Exception(message)
{
    public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
    public bool IsUnauthorized => StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden;
}
