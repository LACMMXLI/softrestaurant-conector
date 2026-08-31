namespace SoftRestaurant.CentralApi;

internal sealed record DashboardLoginRequest(string Email, string Password);

internal static class WebApiEndpoints
{
    public static void MapDashboardWebApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/web");

        group.MapPost("/auth/login", async (
            HttpContext context,
            DashboardLoginRequest request,
            WebAuthService auth,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 320 ||
                string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 1024)
            {
                return Results.BadRequest(new { error = "Correo y contraseña son obligatorios." });
            }

            var login = await auth.LoginAsync(
                request.Email,
                request.Password,
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                ct);
            if (login is null) return Results.Unauthorized();

            WebAuthService.SetSessionCookie(context, login);
            return Results.Ok(new { user = login.User, expiresAt = login.ExpiresAtUtc });
        }).RequireRateLimiting("dashboard-login");

        group.MapPost("/auth/logout", async (
            HttpContext context,
            WebAuthService auth,
            CancellationToken ct) =>
        {
            await auth.LogoutAsync(context, ct);
            WebAuthService.ClearSessionCookie(context);
            return Results.NoContent();
        });

        group.MapGet("/auth/me", async (
            HttpContext context,
            WebAuthService auth,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(new { user });
        });

        group.MapGet("/branches", async (
            HttpContext context,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await reports.GetBranchesAsync(user, ct));
        });

        group.MapGet("/dashboard/home", async (
            HttpContext context,
            string branchCode,
            DateOnly date,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var dashboard = await reports.GetHomeAsync(user, branchCode, date, ct);
            return dashboard is null ? Results.NotFound() : Results.Ok(dashboard);
        });

        group.MapPost("/branches/{branchCode}/request-sync", async (
            HttpContext context,
            string branchCode,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            // VIEWER es de solo lectura: no puede disparar acciones, solo consultar. No se usa
            // Results.Forbid() porque esta app no registra un esquema de autenticación de
            // ASP.NET Core (la sesión se valida a mano en WebAuthService) — devolver 403 directo.
            if (string.Equals(user.Role, "VIEWER", StringComparison.Ordinal))
                return Results.Json(new { error = "Este rol no puede solicitar sincronizaciones." }, statusCode: StatusCodes.Status403Forbidden);

            var requestedAt = await reports.RequestSyncAsync(user, branchCode, ct);
            return requestedAt is null
                ? Results.NotFound()
                : Results.Ok(new { branchCode, syncRequestedAt = requestedAt });
        });

        group.MapGet("/sales", async (
            HttpContext context,
            string branchCode,
            DateOnly date,
            int? page,
            int? pageSize,
            string? search,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var safePage = Math.Max(page ?? 1, 1);
            var safePageSize = Math.Clamp(pageSize ?? 20, 1, 50);
            if (search?.Length > 100) return Results.BadRequest(new { error = "La búsqueda admite máximo 100 caracteres." });

            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var result = await reports.GetSalesAsync(
                user, branchCode, date, safePage, safePageSize, search, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/cash-movements", async (
            HttpContext context,
            string branchCode,
            DateOnly date,
            int? page,
            int? pageSize,
            int? type,
            string? search,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var safePage = Math.Max(page ?? 1, 1);
            var safePageSize = Math.Clamp(pageSize ?? 20, 1, 50);
            if (type is not null and not 1 and not 2)
                return Results.BadRequest(new { error = "El tipo de movimiento debe ser entrada o salida." });
            if (search?.Length > 100)
                return Results.BadRequest(new { error = "La búsqueda admite máximo 100 caracteres." });

            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var result = await reports.GetCashMovementsPageAsync(
                user, branchCode, date, safePage, safePageSize, type, search, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        group.MapGet("/sales/{branchCode}/{folio:long}", async (
            HttpContext context,
            string branchCode,
            long folio,
            WebAuthService auth,
            DashboardReportService reports,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            if (folio <= 0) return Results.BadRequest(new { error = "Folio inválido." });
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var ticket = await reports.GetTicketAsync(user, branchCode, folio, ct);
            return ticket is null ? Results.NotFound() : Results.Ok(ticket);
        });
    }

    private static IResult? ValidateBranchCode(string branchCode) =>
        BranchValidation.IsValidCode(branchCode)
            ? null
            : Results.BadRequest(new { error = "Código de sucursal inválido." });
}
