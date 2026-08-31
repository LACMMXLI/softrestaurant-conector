using SoftRestaurant.CentralApi;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

/// <summary>
/// Prueba la regla de aislamiento por negocio implementada en BusinessAccess (y replicada en
/// SQL por DashboardReportService/BusinessRegistry vía business_members). No hay una instancia
/// de Postgres disponible en este entorno de pruebas, así que estos casos ejercitan
/// directamente la función pura que documenta y decide la regla, en vez de levantar la base de
/// datos real.
///
/// A diferencia del BranchAccess anterior, /api/web/* NUNCA da acceso incondicional a
/// SUPERADMIN — ni siquiera un operador de plataforma ve un negocio del que no es miembro
/// explícito ahí (el acceso "ve todo" es exclusivo de /api/admin/*, fuera del alcance de esta
/// clase).
/// </summary>
public sealed class BusinessAccessTests
{
    [Fact]
    public void Member_can_access_their_own_business_but_not_another()
    {
        var businessA = Guid.NewGuid();
        var businessB = Guid.NewGuid();
        var member = new[] { businessA };

        Assert.True(BusinessAccess.CanAccessBusiness(member, businessA));
        Assert.False(BusinessAccess.CanAccessBusiness(member, businessB));
    }

    [Fact]
    public void Member_of_two_businesses_can_access_both_but_not_a_third()
    {
        var businessA = Guid.NewGuid();
        var businessB = Guid.NewGuid();
        var businessC = Guid.NewGuid();
        var member = new[] { businessA, businessB };

        Assert.True(BusinessAccess.CanAccessBusiness(member, businessA));
        Assert.True(BusinessAccess.CanAccessBusiness(member, businessB));
        Assert.False(BusinessAccess.CanAccessBusiness(member, businessC));
    }

    [Fact]
    public void Non_member_has_no_access_at_all_even_without_any_assignment()
    {
        var business = Guid.NewGuid();

        Assert.False(BusinessAccess.CanAccessBusiness(memberBusinessIds: [], business));
    }

    [Theory]
    [InlineData("OWNER", true)]
    [InlineData("MANAGER", true)]
    [InlineData("VIEWER", false)]
    [InlineData(null, false)]
    public void Only_owner_and_manager_can_manage_a_business(string? role, bool expected) =>
        Assert.Equal(expected, BusinessAccess.CanManageBusiness(role));

    [Fact]
    public void FilterAccessibleBusinesses_returns_only_the_intersection()
    {
        var businessA = Guid.NewGuid();
        var businessB = Guid.NewGuid();
        var businessC = Guid.NewGuid();
        var all = new[] { businessA, businessB, businessC };
        var member = new[] { businessB, Guid.NewGuid() };

        var result = BusinessAccess.FilterAccessibleBusinesses(all, member);

        Assert.Equal([businessB], result);
    }
}
