using System.Text.Json;
using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class ActivityClassifierTests
{
    private static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>();

    private static IReadOnlyDictionary<string, string> Query(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Theory]
    [InlineData("GET", "/api/web/auth/me", "session", "app_open")]
    [InlineData("POST", "/api/web/auth/login", "auth", "auth.login")]
    [InlineData("POST", "/api/web/auth/logout", "auth", "auth.logout")]
    [InlineData("POST", "/api/web/auth/register", "auth", "auth.register")]
    [InlineData("GET", "/api/web/dashboard/home", "view", "dashboard.home")]
    [InlineData("GET", "/api/web/dashboard/business-home", "view", "dashboard.business_home")]
    [InlineData("GET", "/api/web/sales", "view", "sales.list")]
    [InlineData("GET", "/api/web/cash-movements", "view", "operations.cash_movements")]
    [InlineData("GET", "/api/web/product-cancellations", "view", "cancellations.list")]
    [InlineData("POST", "/api/web/businesses", "action", "business.create")]
    [InlineData("GET", "/api/web/businesses", "view", "businesses.list")]
    [InlineData("POST", "/api/web/expenses/categories", "action", "expense_category.create")]
    [InlineData("PUT", "/api/web/expenses/movements/abc/category", "action", "expense.assign_category")]
    [InlineData("POST", "/api/web/branches/centro/request-sync", "action", "sync.request")]
    [InlineData("POST", "/api/web/connector-installations/6f9619ff-8b86-d011-b42d-00c04fc964ff/revoke", "action", "connector.revoke")]
    public void Known_routes_become_stable_actions(string method, string path, string eventType, string action)
    {
        var descriptor = ActivityClassifier.Describe(method, path, NoQuery);

        Assert.NotNull(descriptor);
        Assert.Equal(eventType, descriptor.EventType);
        Assert.Equal(action, descriptor.Action);
    }

    [Theory]
    [InlineData("GET", "/api/web/dashboard/shifts")]   // carga de contexto
    [InlineData("GET", "/api/web/branches")]           // carga de contexto
    [InlineData("POST", "/api/web/activity")]          // el registro no se registra a sí mismo
    [InlineData("GET", "/api/web")]
    [InlineData("GET", "/api/admin/users")]            // fuera de /api/web
    public void Noise_and_foreign_routes_are_skipped(string method, string path)
    {
        Assert.Null(ActivityClassifier.Describe(method, path, NoQuery));
    }

    [Fact]
    public void Branch_and_business_come_from_route_or_query()
    {
        var businessId = Guid.NewGuid();

        var ticket = ActivityClassifier.Describe("GET", "/api/web/sales/centro/1042", NoQuery)!;
        Assert.Equal("centro", ticket.BranchCode);
        Assert.Equal(1042L, ticket.Metadata["folio"]);

        var sales = ActivityClassifier.Describe("GET", "/api/web/sales", Query(("branchCode", "norte")))!;
        Assert.Equal("norte", sales.BranchCode);

        var consolidated = ActivityClassifier.Describe("GET", "/api/web/dashboard/business-home", Query(("businessId", businessId.ToString())))!;
        Assert.Equal(businessId, consolidated.BusinessId);

        var branches = ActivityClassifier.Describe("POST", $"/api/web/businesses/{businessId}/branches", NoQuery)!;
        Assert.Equal("branch.create", branches.Action);
        Assert.Equal(businessId, branches.BusinessId);
    }

    [Fact]
    public void Only_whitelisted_parameters_are_kept_and_free_text_is_never_stored()
    {
        var descriptor = ActivityClassifier.Describe("GET", "/api/web/sales", Query(
            ("branchCode", "centro"), ("date", "2026-10-22"), ("shiftId", "7"), ("page", "2"),
            ("search", "Juan Pérez 555-1234"), ("user", "maria"), ("product", "tacos"), ("secret", "x")))!;

        Assert.Equal("2026-10-22", descriptor.Metadata["date"]);
        Assert.Equal("7", descriptor.Metadata["shiftId"]);
        Assert.Equal(true, descriptor.Metadata["filtered"]);

        var serialized = ActivityLogger.SerializeMetadata(descriptor.Metadata);
        Assert.DoesNotContain("Juan", serialized);
        Assert.DoesNotContain("maria", serialized);
        Assert.DoesNotContain("tacos", serialized);
        Assert.DoesNotContain("secret", serialized);
        Assert.DoesNotContain("search", serialized);
    }

    [Fact]
    public void Failed_login_is_labelled_without_keeping_the_typed_email()
    {
        var login = ActivityClassifier.Describe("POST", "/api/web/auth/login", Query(("email", "alguien@correo.com")))!;

        Assert.Equal("auth.login_failed", ActivityClassifier.Refine(login, 401).Action);
        Assert.Equal("auth.login", ActivityClassifier.Refine(login, 200).Action);
        Assert.DoesNotContain("alguien", ActivityLogger.SerializeMetadata(login.Metadata));
    }

    [Fact]
    public void Client_events_only_accept_known_kinds_and_views()
    {
        var tab = ActivityClassifier.DescribeClient(new ClientActivityRequest("view", "sales", "centro", null, null));
        Assert.NotNull(tab);
        Assert.Equal("ui.tab", tab.Action);
        Assert.Equal("sales", tab.View);

        Assert.Null(ActivityClassifier.DescribeClient(new ClientActivityRequest("drop-table", null, null, null, null)));
        Assert.Null(ActivityClassifier.DescribeClient(new ClientActivityRequest("view", "../etc/passwd", null, null, null)));
        Assert.Null(ActivityClassifier.DescribeClient(new ClientActivityRequest(null, "home", null, null, null)));
    }

    [Fact]
    public void Client_detail_is_trimmed_and_stripped_of_control_characters()
    {
        var long_ = new string('x', 500);
        var descriptor = ActivityClassifier.DescribeClient(new ClientActivityRequest("filter", "home", null, null, "date=2026-10-22\n\t" + long_))!;

        var detail = Assert.IsType<string>(descriptor.Metadata["detail"]);
        Assert.True(detail.Length <= 120);
        Assert.DoesNotContain('\n', detail);
        Assert.DoesNotContain('\t', detail);
    }

    [Fact]
    public void Metadata_serializes_as_a_json_object()
    {
        var descriptor = ActivityClassifier.Describe("GET", "/api/web/sales", Query(("page", "3")))!;

        using var document = JsonDocument.Parse(ActivityLogger.SerializeMetadata(descriptor.Metadata));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal("3", document.RootElement.GetProperty("page").GetString());
    }
}
