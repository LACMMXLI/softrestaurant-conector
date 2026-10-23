namespace RestaurantAgent.CentralApi;

internal static class ActivityEndpoints
{
    public static void MapActivityApi(this WebApplication app, ApiOptions apiOptions)
    {
        // Lo que la propia app reporta (cambio de pestaña, sucursal, fecha...). Los datos de
        // las peticiones a la API ya los registra ActivityTracking; esto cubre la navegación
        // que no genera petición. Solo acepta valores de listas cerradas (ver ActivityClassifier).
        app.MapPost("/api/web/activity", async (
            HttpContext context,
            ClientActivityRequest request,
            WebAuthService auth,
            ActivityLogger logger,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var descriptor = ActivityClassifier.DescribeClient(request);
            if (descriptor is null) return Results.BadRequest(new { error = "Evento de actividad no válido." });
            logger.Record(ActivityTracking.BuildEntry(context, descriptor, user, "client", null, null));
            return Results.NoContent();
        }).RequireRateLimiting("dashboard-activity");

        app.MapGet("/api/admin/activity", async (
            HttpContext context,
            WebAuthService webAuth,
            ActivityReportService reports,
            Guid? userId,
            Guid? businessId,
            string? branchCode,
            string? eventType,
            string? action,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? includeAdmins,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
        {
            if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
            var filter = BuildFilter(userId, businessId, branchCode, eventType, action, from, to, includeAdmins);
            return Results.Ok(await reports.ListAsync(filter, page ?? 1, pageSize ?? 50, ct));
        });

        app.MapGet("/api/admin/activity/summary", async (
            HttpContext context,
            WebAuthService webAuth,
            ActivityReportService reports,
            Guid? userId,
            Guid? businessId,
            string? branchCode,
            DateTimeOffset? from,
            DateTimeOffset? to,
            bool? includeAdmins,
            string? tz,
            CancellationToken ct) =>
        {
            if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
            var filter = BuildFilter(userId, businessId, branchCode, null, null, from, to, includeAdmins);
            return Results.Ok(await reports.GetSummaryAsync(filter, ActivityClassifier.Clean(tz, 64), ct));
        });
    }

    internal static ActivityFilter BuildFilter(
        Guid? userId, Guid? businessId, string? branchCode, string? eventType, string? action,
        DateTimeOffset? from, DateTimeOffset? to, bool? includeAdmins) =>
        new(userId, businessId,
            ActivityClassifier.Clean(branchCode, 100),
            ActivityClassifier.Clean(eventType, 32),
            ActivityClassifier.Clean(action, 64),
            from?.UtcDateTime, to?.UtcDateTime, includeAdmins ?? false);
}
