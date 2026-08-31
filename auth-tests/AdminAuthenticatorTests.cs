using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class AdminAuthenticatorTests
{
    [Fact]
    public async Task No_session_is_rejected()
    {
        var options = BuildOptions();
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=admin-auth-tests-unreachable;Database=x;Username=x;Password=x;Timeout=1");
        var auth = new WebAuthService(dataSource, options);
        var context = new DefaultHttpContext();
        var principal = await AdminAuthenticator.AuthorizeAsync(context, options, auth, CancellationToken.None);

        Assert.Null(principal);
    }

    [Fact]
    public async Task No_session_is_rejected_by_boolean_helper()
    {
        var options = BuildOptions();
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=admin-auth-tests-unreachable;Database=x;Username=x;Password=x;Timeout=1");
        var auth = new WebAuthService(dataSource, options);
        var context = new DefaultHttpContext();

        Assert.False(await AdminAuthenticator.IsAuthorizedAsync(context, options, auth, CancellationToken.None));
    }

    private static ApiOptions BuildOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] =
                "Host=admin-auth-tests-unreachable;Database=x;Username=x;Password=x;Timeout=1"
        }).Build();
        return ApiOptions.FromConfiguration(configuration);
    }
}
