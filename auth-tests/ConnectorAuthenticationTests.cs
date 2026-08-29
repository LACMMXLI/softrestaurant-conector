using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using SoftRestaurant.CentralApi;
using SoftRestaurant.Extractor;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

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
    public void New_connector_requests_identify_connector_and_use_bearer_token()
    {
        var connectorId = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage();

        AgentApiClient.ApplyAuthentication(request, "sra_conn_test-token", connectorId);

        Assert.Equal(connectorId, request.Headers.GetValues("X-Connector-Id").Single());
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sra_conn_test-token"), request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-Agent-Token"));
    }

    [Fact]
    public void Legacy_header_is_used_only_without_connector_identity()
    {
        using var request = new HttpRequestMessage();

        AgentApiClient.ApplyAuthentication(request, "legacy-test", connectorId: null);

        Assert.Equal("legacy-test", request.Headers.GetValues("X-Agent-Token").Single());
        Assert.False(request.Headers.Contains("X-Connector-Id"));
        Assert.Null(request.Headers.Authorization);
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
    public void Dpapi_replaces_one_time_key_with_connector_credential()
    {
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
                  "SRX_ACTIVATION_KEY":"sra_act_one-time-test",
                  "SRX_MACHINE_NAME":"CAJA-TEST",
                  "SRX_SQL_SERVER":"localhost",
                  "SRX_SQL_DATABASE":"softrestaurant-test",
                  "SRX_SQL_USER":"test",
                  "SRX_SQL_PASSWORD":"test-password"
                }
                """, Encoding.UTF8);
            ProtectedSettings.ProtectFile(input, protectedPath);
            Environment.SetEnvironmentVariable("SRX_PROTECTED_CONFIG", protectedPath);

            ProtectedSettings.CompleteActivation(
                "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "sucursal-test", "sra_conn_permanent-test");
            var loaded = ProtectedSettings.Load();

            Assert.False(loaded.ContainsKey("SRX_ACTIVATION_KEY"));
            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", loaded["SRX_CONNECTOR_ID"]);
            Assert.Equal("sucursal-test", loaded["SRX_BRANCH_CODE"]);
            Assert.Equal("sra_conn_permanent-test", loaded["SRX_AGENT_TOKEN"]);
            Assert.DoesNotContain("sra_conn_permanent-test", Encoding.UTF8.GetString(File.ReadAllBytes(protectedPath)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SRX_PROTECTED_CONFIG", previousPath);
            Directory.Delete(directory, recursive: true);
        }
    }
}
