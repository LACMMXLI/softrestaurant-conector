namespace SoftRestaurant.CentralApi;

/// <summary>
/// Regla de autorización única para "¿puede este usuario ver esta sucursal en /api/web/*?".
/// <see cref="DashboardReportService"/> implementa exactamente esta misma regla en SQL
/// (columna por columna: <c>$1 OR EXISTS(... app_user_branches ...)</c>) porque filtrar del
/// lado del servidor de base de datos es más barato que traer todo y filtrar en memoria.
/// Esta clase existe para que la regla tenga UNA definición legible y con tests unitarios
/// (<c>BranchAccessTests</c>) que no dependan de una instancia real de Postgres; si cambia
/// aquí, la consulta SQL correspondiente debe cambiar igual.
/// </summary>
internal static class BranchAccess
{
    /// <summary>
    /// SUPERADMIN tiene acceso incondicional (no necesita filas en app_user_branches).
    /// Cualquier otro rol —incluido OWNER— solo accede a sucursales asignadas explícitamente.
    /// </summary>
    public static bool CanAccessBranch(
        DashboardUser user, IReadOnlyCollection<string> assignedBranchCodes, string branchCode) =>
        user.IsSuperAdmin || assignedBranchCodes.Contains(branchCode);

    /// <summary>Filtra una lista de sucursales activas a las que el usuario puede ver.</summary>
    public static IEnumerable<string> FilterAccessibleBranches(
        DashboardUser user,
        IReadOnlyCollection<string> allActiveBranchCodes,
        IReadOnlyCollection<string> assignedBranchCodes) =>
        user.IsSuperAdmin
            ? allActiveBranchCodes
            : allActiveBranchCodes.Where(assignedBranchCodes.Contains);
}
