using SoftRestaurant.CentralApi;
using Xunit;

namespace SoftRestaurant.Auth.Tests;

public sealed class SuperAdminGuardTests
{
    [Fact]
    public void Blocks_deactivating_the_last_active_superadmin()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: null, newActive: false,
            otherActiveSuperAdminCount: 0);

        Assert.True(blocked);
    }

    [Fact]
    public void Allows_deactivating_a_superadmin_when_another_active_superadmin_exists()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: null, newActive: false,
            otherActiveSuperAdminCount: 1);

        Assert.False(blocked);
    }

    [Fact]
    public void Blocks_demoting_the_last_active_superadmin_to_owner()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: "OWNER", newActive: null,
            otherActiveSuperAdminCount: 0);

        Assert.True(blocked);
    }

    [Fact]
    public void Allows_demoting_a_superadmin_when_another_active_superadmin_exists()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: "OWNER", newActive: null,
            otherActiveSuperAdminCount: 2);

        Assert.False(blocked);
    }

    [Fact]
    public void Allows_deactivating_and_demoting_at_the_same_time_when_covered()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: "OWNER", newActive: false,
            otherActiveSuperAdminCount: 1);

        Assert.False(blocked);
    }

    [Fact]
    public void Blocks_deactivating_and_demoting_the_last_one_at_the_same_time()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: true,
            newRole: "OWNER", newActive: false,
            otherActiveSuperAdminCount: 0);

        Assert.True(blocked);
    }

    [Fact]
    public void Never_blocks_changes_to_a_non_superadmin_account()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "OWNER", currentlyActive: true,
            newRole: null, newActive: false,
            otherActiveSuperAdminCount: 0);

        Assert.False(blocked);
    }

    [Fact]
    public void Does_not_block_touching_an_already_inactive_superadmin()
    {
        // Un SUPERADMIN ya inactivo no está protegiendo nada ahora mismo: cambiarle el rol
        // no reduce el número de SUPERADMIN activos.
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: false,
            newRole: "OWNER", newActive: null,
            otherActiveSuperAdminCount: 0);

        Assert.False(blocked);
    }

    [Fact]
    public void Never_blocks_promoting_someone_to_superadmin()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "OWNER", currentlyActive: true,
            newRole: "SUPERADMIN", newActive: null,
            otherActiveSuperAdminCount: 0);

        Assert.False(blocked);
    }

    [Fact]
    public void Never_blocks_reactivating_a_superadmin()
    {
        var blocked = SuperAdminGuard.WouldRemoveLastActiveSuperAdmin(
            currentRole: "SUPERADMIN", currentlyActive: false,
            newRole: null, newActive: true,
            otherActiveSuperAdminCount: 0);

        Assert.False(blocked);
    }
}
