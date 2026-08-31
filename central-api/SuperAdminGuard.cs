namespace RestaurantAgent.CentralApi;

/// <summary>
/// Decide si desactivar o cambiar el rol de una cuenta dejaría al sistema sin ningún
/// SUPERADMIN activo. Es una función pura para poder probar todos los casos límite sin
/// depender de una base de datos real; <see cref="UserRegistry"/> la evalúa dentro de una
/// transacción con el conteo de "otros SUPERADMIN activos" leído con bloqueo de fila, para
/// que la comprobación sea atómica frente a modificaciones concurrentes.
/// </summary>
internal static class SuperAdminGuard
{
    /// <param name="currentRole">Rol actual de la cuenta objetivo.</param>
    /// <param name="currentlyActive">Estado actual de la cuenta objetivo.</param>
    /// <param name="newRole">Rol propuesto, o null si la operación no cambia el rol.</param>
    /// <param name="newActive">Estado propuesto, o null si la operación no cambia el estado.</param>
    /// <param name="otherActiveSuperAdminCount">
    /// Cuántas OTRAS cuentas (distintas de la objetivo) son SUPERADMIN y están activas.
    /// </param>
    /// <returns>true si la operación debe bloquearse.</returns>
    public static bool WouldRemoveLastActiveSuperAdmin(
        string currentRole,
        bool currentlyActive,
        string? newRole,
        bool? newActive,
        int otherActiveSuperAdminCount)
    {
        var wasProtecting = IsSuperAdmin(currentRole) && currentlyActive;
        if (!wasProtecting) return false; // no era un SUPERADMIN activo: nada que proteger

        var staysProtecting = IsSuperAdmin(newRole ?? currentRole) && (newActive ?? currentlyActive);
        if (staysProtecting) return false; // sigue siendo SUPERADMIN activo tras el cambio

        return otherActiveSuperAdminCount == 0;
    }

    private static bool IsSuperAdmin(string role) =>
        string.Equals(role, "SUPERADMIN", StringComparison.Ordinal);
}
