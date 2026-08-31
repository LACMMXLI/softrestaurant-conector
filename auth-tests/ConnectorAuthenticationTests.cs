using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using RestaurantAgent.CentralApi;
using RestaurantAgent.Extractor;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class ConnectorAuthenticationTests
{
    [Fact]
    public void Generated_tokens_are_unique_and_only_hashes_need_persisting()
    {
        var first = TokenHasher.Generate("sra_conn_");
        var second = TokenHasher.Generate("sra_conn_");

        Assert.StartsWith("sra_conn_", first);
        Assert.NotEqual(first, second);
        Assert.Equal(64, TokenHasher.Hash(first).Length);
        Assert.True(TokenHasher.FixedTimeEquals(first, first));
        Assert.False(TokenHasher.FixedTimeEquals(first, second));
    }

    [Fact]
    public void Device_requests_always_identify_installation_and_use_bearer_token()
    {
        var installationId = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage();

        AgentApiClient.ApplyAuthentication(request, "sra_conn_test-token", installationId);

        Assert.Equal(installationId, request.Headers.GetValues("X-Connector-Id").Single());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sra_conn_test-token"), request.Headers.Authorization);
        // Ya no existe el modo legacy de token compartido por sucursal.
        Assert.False(request.Headers.Contains("X-Agent-Token"));
    }

    [Fact]
    public void Admin_key_must_be_internal_and_at_least_32_characters()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = "Host=test;Database=test;Username=test;Password=test",
            ["CONNECTOR_ADMIN_KEY"] = "short"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => ApiOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void Installer_protects_sql_config_without_requiring_any_device_credential()
    {
        // Desde el modelo SaaS, el instalador nunca recoge credencial de dispositivo — solo
        // conexión SQL + URL de API. La identidad de dispositivo llega después, vía POST /link
        // (ver AgentControlServer), no en el momento de proteger el archivo.
        if (!OperatingSystem.IsWindows()) return;

        var directory = Path.Combine(Path.GetTempPath(), "srx-auth-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "settings.json");
        var protectedPath = Path.Combine(directory, "settings.dpapi");
        var previousPath = Environment.GetEnvironmentVariable("SRX_PROTECTED_CONFIG");
        try
        {
            File.WriteAllText(input, """
                {
                  "SRX_API_URL":"https://api.example.test",
                  "SRX_SQL_SERVER":"localhost",
                  "SRX_SQL_DATABASE":"restaurant-agent-test",
                  "SRX_SQL_USER":"test",
                  "SRX_SQL_PASSWORD":"test-password"
                }
                """, Encoding.UTF8);
            ProtectedSettings.ProtectFile(input, protectedPath);
            Environment.SetEnvironmentVariable("SRX_PROTECTED_CONFIG", protectedPath);

            var loadedBeforeLink = ProtectedSettings.Load();
            Assert.False(loadedBeforeLink.ContainsKey("SRX_DEVICE_TOKEN"));
            Assert.True(ProtectedSettings.TryLoadValid(protectedPath, out _));

            ProtectedSettings.ApplyLink(
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "sucursal-test", "bbbbbbbb-cccc-dddd-eeee-ffffffffffff",
                "sra_conn_permanent-test", apiUrl: null);
            var loadedAfterLink = ProtectedSettings.Load();

            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", loadedAfterLink["SRX_INSTALLATION_ID"]);
            Assert.Equal("sucursal-test", loadedAfterLink["SRX_BRANCH_CODE"]);
            Assert.Equal("sra_conn_permanent-test", loadedAfterLink["SRX_DEVICE_TOKEN"]);
            // La conexión SQL protegida antes de vincular sobrevive intacta.
            Assert.Equal("localhost", loadedAfterLink["SRX_SQL_SERVER"]);
            Assert.DoesNotContain("sra_conn_permanent-test", Encoding.UTF8.GetString(File.ReadAllBytes(protectedPath)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SRX_PROTECTED_CONFIG", previousPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}
