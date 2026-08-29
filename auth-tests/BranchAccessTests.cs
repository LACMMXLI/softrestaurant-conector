using SoftRestaurant.CentralApi;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

/// <summary>
/// Prueba la regla de aislamiento por sucursal implementada en BranchAccess (y replicada en
/// SQL por DashboardReportService). No hay una instancia de Postgres disponible en este
/// entorno de pruebas, así que estos casos ejercitan directamente la función pura que
/// documenta y decide la regla, en vez de levantar la base de datos real.
/// </summary>
public sealed class BranchAccessTests
{
    private static DashboardUser MakeUser(string role) =>
        new(Guid.NewGuid(), $"{role.ToLowerInvariant()}@example.test", role, role);

    [Fact]
    public void SuperAdmin_can_access_any_branch_without_any_assignment()
    {
        var superAdmin = MakeUser("SUPERADMIN");

        Assert.True(BranchAccess.CanAccessBranch(superAdmin, assignedBranchCodes: [], "sucursal-a"));
        Assert.True(BranchAccess.CanAccessBranch(superAdmin, assignedBranchCodes: [], "sucursal-b"));
    }

    [Fact]
    public void Owner_assigned_to_branch_a_cannot_access_branch_b()
    {
        var owner = MakeUser("OWNER");
        var assigned = new[] { "sucursal-a" };

        Assert.True(BranchAccess.CanAccessBranch(owner, assigned, "sucursal-a"));
        Assert.False(BranchAccess.CanAccessBranch(owner, assigned, "sucursal-b"));
    }

    [Fact]
    public void Owner_with_two_branches_can_access_both_but_not_a_third()
    {
        var owner = MakeUser("OWNER");
        var assigned = new[] { "sucursal-a", "sucursal-b" };

        Assert.True(BranchAccess.CanAccessBranch(owner, assigned, "sucursal-a"));
        Assert.True(BranchAccess.CanAccessBranch(owner, assigned, "sucursal-b"));
        Assert.False(BranchAccess.CanAccessBranch(owner, assigned, "sucursal-c"));
    }

    [Fact]
    public void Owner_without_any_assignment_has_no_access_at_all()
    {
        // Regresión: antes de esta fase, OWNER veía TODAS las sucursales sin necesitar
        // app_user_branches. Este caso es exactamente el bug que se corrigió.
        var owner = MakeUser("OWNER");

        Assert.False(BranchAccess.CanAccessBranch(owner, assignedBranchCodes: [], "sucursal-a"));
        Assert.False(BranchAccess.CanAccessBranch(owner, assignedBranchCodes: [], "sucursal-b"));
    }

    [Theory]
    [InlineData("MANAGER")]
    [InlineData("VIEWER")]
    public void Manager_and_viewer_are_isolated_to_their_assigned_branches(string role)
    {
        var user = MakeUser(role);
        var assigned = new[] { "sucursal-a" };

        Assert.True(BranchAccess.CanAccessBranch(user, assigned, "sucursal-a"));
        Assert.False(BranchAccess.CanAccessBranch(user, assigned, "sucursal-b"));
    }

    [Fact]
    public void FilterAccessibleBranches_returns_everything_for_superadmin()
    {
        var superAdmin = MakeUser("SUPERADMIN");
        var all = new[] { "sucursal-a", "sucursal-b", "sucursal-c" };

        var result = BranchAccess.FilterAccessibleBranches(superAdmin, all, assignedBranchCodes: []);

        Assert.Equal(all, result);
    }

    [Fact]
    public void FilterAccessibleBranches_returns_only_the_intersection_for_owner()
    {
        var owner = MakeUser("OWNER");
        var all = new[] { "sucursal-a", "sucursal-b", "sucursal-c" };
        var assigned = new[] { "sucursal-b", "sucursal-does-not-exist" };

        var result = BranchAccess.FilterAccessibleBranches(owner, all, assigned);

        Assert.Equal(["sucursal-b"], result);
    }
}
