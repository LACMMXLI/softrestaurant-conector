using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoftRestaurant.Extractor.Ui;

public sealed class DashboardUserDto
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class BusinessMembershipDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public bool Active { get; set; }
    public string Role { get; set; } = "";
}

public sealed class BranchDto
{
    public Guid Id { get; set; }
    public Guid BusinessId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class ConnectorInstallationDto
{
    public Guid Id { get; set; }
    public string BranchCode { get; set; } = "";
    public string MachineName { get; set; } = "";
    public bool Active { get; set; }
    public string? AgentVersion { get; set; }
    public DateTime? LinkedAt { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastStatus { get; set; }
    public string? LastError { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class BranchWithConnectorDto
{
    public BranchDto Branch { get; set; } = new();
    public ConnectorInstallationDto? Connector { get; set; }
}

public sealed class DeviceCredentialDto
{
    public Guid InstallationId { get; set; }
    public string BranchCode { get; set; } = "";
    public Guid BusinessId { get; set; }
    public string Token { get; set; } = "";
    public string? ApiUrl { get; set; }
}

public sealed class LinkConflictDto
{
    public string? Error { get; set; }
    public ConnectorInstallationDto? ActiveInstallation { get; set; }
}

/// <summary>Resultado de intentar vincular: éxito, o conflicto porque la sucursal ya tiene un conector activo (requiere "Reemplazar equipo").</summary>
public sealed class LinkDeviceOutcome
{
    public DeviceCredentialDto? Credential { get; init; }
    public ConnectorInstallationDto? ExistingActive { get; init; }
    public bool Succeeded => Credential is not null;

    public static LinkDeviceOutcome Ok(DeviceCredentialDto credential) => new() { Credential = credential };
    public static LinkDeviceOutcome Conflict(ConnectorInstallationDto? active) => new() { ExistingActive = active };
}

/// <summary>
/// Cliente HTTP directo contra central-api (no contra el control local del servicio): la GUI
/// inicia sesión con la cuenta humana del usuario para listar sus negocios/sucursales y pedir la
/// vinculación — exactamente lo mismo que haría dashboard-web desde el navegador, usando la
/// misma sesión por cookie. El token de sesión humano NUNCA se comparte con el servicio: solo la
/// credencial de dispositivo que resulta de vincular viaja hacia AgentControlServer (ver
/// LinkDeviceForm.cs).
/// </summary>
public sealed class CentralApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient client;

    public CentralApiClient(string apiUrl)
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        client = new HttpClient(handler)
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<(bool Ok, string? Error)> LoginAsync(string email, string password, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            "api/web/auth/login", new { email, password }, JsonOptions, ct);
        if (response.IsSuccessStatusCode) return (true, null);
        return (false, await ReadErrorAsync(response, ct));
    }

    public async Task<List<BusinessMembershipDto>> GetBusinessesAsync(CancellationToken ct) =>
        await client.GetFromJsonAsync<List<BusinessMembershipDto>>("api/web/businesses", JsonOptions, ct)
            ?? [];

    public async Task<List<BranchWithConnectorDto>> GetBranchesAsync(Guid businessId, CancellationToken ct) =>
        await client.GetFromJsonAsync<List<BranchWithConnectorDto>>(
            $"api/web/businesses/{businessId}/branches", JsonOptions, ct) ?? [];

    public async Task<LinkDeviceOutcome> LinkDeviceAsync(string branchCode, string machineName, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            $"api/web/branches/{branchCode}/link-device", new { machineName }, JsonOptions, ct);
        if (response.IsSuccessStatusCode)
        {
            var credential = await response.Content.ReadFromJsonAsync<DeviceCredentialDto>(JsonOptions, ct)
                ?? throw new HttpRequestException("central-api no devolvió la credencial del dispositivo.");
            return LinkDeviceOutcome.Ok(credential);
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadFromJsonAsync<LinkConflictDto>(JsonOptions, ct);
            return LinkDeviceOutcome.Conflict(conflict?.ActiveInstallation);
        }
        throw new HttpRequestException(await ReadErrorAsync(response, ct) ?? $"Error {(int)response.StatusCode}.");
    }

    public async Task<DeviceCredentialDto> ReplaceDeviceAsync(string branchCode, string machineName, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            $"api/web/branches/{branchCode}/replace-device", new { machineName }, JsonOptions, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await ReadErrorAsync(response, ct) ?? $"Error {(int)response.StatusCode}.");
        return await response.Content.ReadFromJsonAsync<DeviceCredentialDto>(JsonOptions, ct)
            ?? throw new HttpRequestException("central-api no devolvió la credencial del dispositivo.");
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            return body.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => client.Dispose();
}
