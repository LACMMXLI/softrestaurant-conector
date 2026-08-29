using System.Net.Http.Json;

namespace SoftRestaurant.Extractor;

internal sealed record ConnectorActivationResponse(Guid ConnectorId, string BranchCode, string Token);

internal static class AgentRegistrationClient
{
    public static async Task EnsureActivatedAsync(ExtractorConfig config, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(config.AgentToken)) return;
        if (string.IsNullOrWhiteSpace(config.ActivationKey))
            throw new InvalidOperationException("El agente no tiene credencial ni clave de activación.");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
        using var response = await client.PostAsJsonAsync(
            $"{config.ApiUrl!.TrimEnd('/')}/api/connectors/activate",
            new
            {
                activationKey = config.ActivationKey,
                machineName = config.MachineName,
                agentVersion = typeof(AgentRegistrationClient).Assembly.GetName().Version?.ToString(),
                metadata = new
                {
                    os = Environment.OSVersion.VersionString,
                    architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()
                }
            },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Activación rechazada: {(int)response.StatusCode} {body[..Math.Min(body.Length, 500)]}");
        }

        var activation = await response.Content.ReadFromJsonAsync<ConnectorActivationResponse>(cancellationToken: ct)
            ?? throw new HttpRequestException("La API no devolvió la credencial del conector.");
        ProtectedSettings.CompleteActivation(
            activation.ConnectorId.ToString(), activation.BranchCode, activation.Token);
        config.CompleteActivation(
            activation.ConnectorId.ToString(), activation.BranchCode, activation.Token);
        Console.WriteLine($"Conector activado. Id={activation.ConnectorId}, sucursal={activation.BranchCode}.");
    }
}
