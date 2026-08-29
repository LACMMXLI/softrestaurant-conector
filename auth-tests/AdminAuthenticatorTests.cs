using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SoftRestaurant.CentralApi;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

public sealed class AdminAuthenticatorTests
{
    // Host inalcanzable a propósito: estas pruebas solo ejercitan las rutas que resuelven
    // sin abrir una conexión real (llave estática válida, o ausencia de cookie de sesión).
    private const string UnreachableConnectionString =
        "Host=admin-auth-tests-unreachable;Database=x;Username=x;Password=x;Timeout=1";

    [Fact]
    public async Task Valid_static_admin_key_authorizes_without_touching_the_session()
    {
        var options = BuildOptions(adminKey: new string('a', 32));
        var auth = new WebAuthService(NpgsqlDataSource.Create(UnreachableConnectionString), options);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Key"] = options.ConnectorAdminKey;

        var authorized = await AdminAuthenticator.IsAuthorizedAsync(context, options, auth, CancellationToken.None);

        Assert.True(authorized);
    }

    [Fact]
    public async Task Wrong_admin_key_falls_through_to_session_check_and_is_rejected_without_cookie()
    {
        var options = BuildOptions(adminKey: new string('a', 32));
        var auth = new WebAuthService(NpgsqlDataSource.Create(UnreachableConnectionString), options);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Key"] = "not-the-right-key";

        var authorized = await AdminAuthenticator.IsAuthorizedAsync(context, options, auth, CancellationToken.None);

        Assert.False(authorized);
    }

    [Fact]
    public async Task No_admin_key_and_no_session_cookie_is_rejected()
    {
        var options = BuildOptions(adminKey: string.Empty);
        var auth = new WebAuthService(NpgsqlDataSource.Create(UnreachableConnectionString), options);
        var context = new DefaultHttpContext();

        var authorized = await AdminAuthenticator.IsAuthorizedAsync(context, options, auth, CancellationToken.None);

        Assert.False(authorized);
    }

    [Fact]
    public async Task Static_admin_key_authorizes_as_a_principal_without_a_user()
    {
        var options = BuildOptions(adminKey: new string('a', 32));
        var auth = new WebAuthService(NpgsqlDataSource.Create(UnreachableConnectionString), options);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Admin-Key"] = options.ConnectorAdminKey;

        var principal = await AdminAuthenticator.AuthorizeAsync(context, options, auth, CancellationToken.None);

        Assert.NotNull(principal);
        Assert.Null(principal!.User); // sin sesión: no hay "propia cuenta" que comparar en selfAffected
    }

    [Fact]
    public async Task No_admin_key_and_no_session_cookie_returns_no_principal()
    {
        var options = BuildOptions(adminKey: string.Empty);
        var auth = new WebAuthService(NpgsqlDataSource.Create(UnreachableConnectionString), options);
        var context = new DefaultHttpContext();

        var principal = await AdminAuthenticator.AuthorizeAsync(context, options, auth, CancellationToken.None);

        Assert.Null(principal);
    }

    private static ApiOptions BuildOptions(string adminKey)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = UnreachableConnectionString,
            ["CONNECTOR_ADMIN_KEY"] = adminKey,
        }).Build();
        return ApiOptions.FromConfiguration(configuration);
    }
}
