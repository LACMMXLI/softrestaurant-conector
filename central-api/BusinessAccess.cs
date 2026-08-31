namespace SoftRestaurant.CentralApi;

/// <summary>
/// Regla de autorización única para "¿puede este usuario ver/administrar este negocio (y por
/// tanto sus sucursales) en /api/web/*?". <see cref="BusinessRegistry"/> y
/// <see cref="DashboardReportService"/> implementan exactamente esta misma regla en SQL
/// (<c>EXISTS(... business_members ...)</c>) porque filtrar del lado del servidor de base de
/// datos es más barato que traer todo y filtrar en memoria. Esta clase existe para que la regla
/// tenga UNA definición legible y con tests unitarios (<c>BusinessAccessTests</c>) que no
/// dependan de una instancia real de Postgres; si cambia aquí, las consultas SQL
/// correspondientes deben cambiar igual.
///
/// A diferencia de /api/admin/* (donde SUPERADMIN tiene acceso incondicional vía
/// <see cref="AdminAuthenticator"/>), las rutas de autogestión /api/web/* NUNCA usan ese atajo:
/// un SUPERADMIN solo ve los negocios de los que es miembro explícito, igual que cualquier otra
/// cuenta. El acceso "ve todo" queda reservado exclusivamente al panel de operador.
/// </summary>
internal static class BusinessAccess
{
    public static bool CanAccessBusiness(
        IReadOnlyCollection<Guid> memberBusinessIds, Guid businessId) =>
        memberBusinessIds.Contains(businessId);

    /// <summary>OWNER y MANAGER pueden crear/editar recursos del negocio; VIEWER es de solo lectura.</summary>
    public static bool CanManageBusiness(string? role) =>
        role is "OWNER" or "MANAGER";

    public static IEnumerable<Guid> FilterAccessibleBusinesses(
        IReadOnlyCollection<Guid> allBusinessIds,
        IReadOnlyCollection<Guid> memberBusinessIds) =>
        allBusinessIds.Where(memberBusinessIds.Contains);
}
