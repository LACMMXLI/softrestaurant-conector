using System.Net.Http.Json;
using System.Net.Http.Headers;
using SoftRestaurant.Sync.Contracts;

namespace SoftRestaurant.Extractor;

internal sealed class AgentApiClient(string apiUrl, string token, string? connectorId)
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
        ApplyAuthentication(request, token, connectorId);
        using var response = await client.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"API respondió {(int)response.StatusCode} {response.ReasonPhrase}: {body[..Math.Min(body.Length, 1000)]}");
    }

    internal static void ApplyAuthentication(
        HttpRequestMessage request, string token, string? connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
        {
            // LEGACY: compatibilidad temporal con el token compartido por sucursal.
            request.Headers.Add("X-Agent-Token", token);
        }
        else
        {
            request.Headers.Add("X-Connector-Id", connectorId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
