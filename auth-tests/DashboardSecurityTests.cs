using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class DashboardSecurityTests
{
    [Fact]
    public void Coverage_distinguishes_complete_partial_missing_and_invalid_ranges()
    {
        var date = new DateOnly(2026, 8, 29);
        var start = date.ToDateTime(TimeOnly.MinValue);

        Assert.Equal("missing", DashboardReportService.GetCoverage(date, null, null, null, null));
        Assert.Equal("invalid", DashboardReportService.GetCoverage(date, "batch", start, start.AddDays(1), false));
        Assert.Equal("partial", DashboardReportService.GetCoverage(date, "batch", start, start.AddHours(12), true));
        Assert.Equal("complete", DashboardReportService.GetCoverage(date, "batch", start, start.AddDays(1), true));
        Assert.Equal("missing", DashboardReportService.GetCoverage(date, "batch", start.AddDays(1), start.AddDays(2), true));
    }

    [Fact]
    public void User_credentials_in_environment_are_ignored_and_do_not_block_startup()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["LEGACY_OWNER_EMAIL"] = "legacy@example.test",
            ["LEGACY_OWNER_PASSWORD"] = "legacy-password",
            ["LEGACY_ADMIN_EMAIL"] = "legacy-admin@example.test",
            ["LEGACY_ADMIN_PASSWORD"] = "legacy-password"
        });

        var options = ApiOptions.FromConfiguration(configuration);

        Assert.Equal(24, options.DashboardSessionHours);
        Assert.Equal(10, options.DashboardStaleMinutes);
    }

    [Fact]
    public void Dashboard_session_cookie_is_http_only_secure_and_same_site()
    {
        var options = WebAuthService.CreateSessionCookieOptions(
            isHttps: true,
            DateTime.UtcNow.AddHours(1));

        Assert.True(options.HttpOnly);
        Assert.True(options.Secure);
        Assert.Equal(SameSiteMode.Lax, options.SameSite);
        Assert.Equal("/", options.Path);
        Assert.Null(options.Domain);
    }

    [Fact]
    public void Dashboard_limits_have_safe_defaults_and_bounds()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DASHBOARD_SESSION_HOURS"] = "9999",
            ["DASHBOARD_STALE_MINUTES"] = "0"
        });

        var options = ApiOptions.FromConfiguration(configuration);

        Assert.Equal(24, options.DashboardSessionHours);
        Assert.Equal(10, options.DashboardStaleMinutes);
    }

    [Fact]
    public void Dashboard_uses_business_shift_number_instead_of_internal_shift_id()
    {
        Assert.Equal(426, DashboardReportService.ResolveBusinessShiftNumber(610301, 426));
    }

    [Fact]
    public void Dashboard_keeps_legacy_shift_rows_without_business_shift_number_compatible()
    {
        Assert.Equal(420, DashboardReportService.ResolveBusinessShiftNumber(420, null));
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:Database"] =
            "Host=test;Database=test;Username=test;Password=test";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
