namespace RestaurantAgent.CentralApi;

internal sealed record DashboardLoginRequest(string Email, string Password);
internal sealed record DashboardRegisterRequest(string Email, string Password, string DisplayName);
internal sealed record CreateBusinessRequest(string Name);
internal sealed record CreateBranchSelfServiceRequest(string Code, string Name, string? Timezone);

internal static class WebApiEndpoints
{
    public static void MapDashboardWebApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/web");

        group.MapPost("/auth/register", async (
            HttpContext context,
            DashboardRegisterRequest request,
            WebAuthService auth,
            CancellationToken ct) =>
        {
            var email = request.Email?.Trim() ?? string.Empty;
            if (!UserValidation.IsValidEmail(email))
                return Results.BadRequest(new { error = "Correo inválido." });
            if (!UserValidation.IsValidPassword(request.Password))
                return Results.BadRequest(new { error = $"La contraseña debe tener al menos {UserValidation.MinPasswordLength} caracteres." });
            if (!UserValidation.IsValidDisplayName(request.DisplayName))
                return Results.BadRequest(new { error = "El nombre es obligatorio." });

            var result = await auth.RegisterAsync(
                email,
                request.Password,
                request.DisplayName.Trim(),
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                ct);
            if (result.Status == RegisterStatus.EmailTaken)
                return Results.Conflict(new { error = "Ya existe una cuenta con ese correo." });

            var login = result.Login!;
            WebAuthService.SetSessionCookie(context, login);
            return Results.Ok(new { user = login.User, expiresAt = login.ExpiresAtUtc });
        }).RequireRateLimiting("dashboard-login");

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

        // ── Negocios (Account → Business) ───────────────────────────────────────────────────

        group.MapGet("/businesses", async (
            HttpContext context,
            WebAuthService auth,
            BusinessRegistry businesses,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(await businesses.GetMyBusinessesAsync(user.Id, ct));
        });

        group.MapPost("/businesses", async (
            HttpContext context,
            CreateBusinessRequest request,
            WebAuthService auth,
            BusinessRegistry businesses,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > BranchValidation.MaxNameLength)
                return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });

            var created = await businesses.CreateBusinessAsync(user.Id, request.Name.Trim(), ct);
            return Results.Created($"/api/web/businesses/{created.Id}", created);
        });

        // ── Sucursales de un negocio (Business → Branch) ────────────────────────────────────

        group.MapGet("/businesses/{businessId:guid}/branches", async (
            HttpContext context,
            Guid businessId,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branches,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            if (await businesses.GetMemberRoleAsync(businessId, user.Id, ct) is null)
                return Results.NotFound();

            var branchList = await branches.GetBranchesForBusinessAsync(businessId, ct);
            var withConnector = new List<object>();
            foreach (var branch in branchList)
            {
                var active = await installations.GetActiveInstallationAsync(branch.Id, ct);
                withConnector.Add(new { branch, connector = active });
            }
            return Results.Ok(withConnector);
        });

        group.MapPost("/businesses/{businessId:guid}/branches", async (
            HttpContext context,
            Guid businessId,
            CreateBranchSelfServiceRequest request,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branches,
            CancellationToken ct) =>
        {
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();
            var role = await businesses.GetMemberRoleAsync(businessId, user.Id, ct);
            if (role is null) return Results.NotFound();
            if (!BusinessAccess.CanManageBusiness(role))
                return Results.Json(new { error = "Este rol no puede crear sucursales." }, statusCode: StatusCodes.Status403Forbidden);

            var code = request.Code?.Trim() ?? string.Empty;
            if (!BranchValidation.IsValidCode(code))
                return Results.BadRequest(new { error = "code es obligatorio: minúsculas, dígitos y guiones, 2 a 63 caracteres" });
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > BranchValidation.MaxNameLength)
                return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });
            var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "America/Tijuana" : request.Timezone.Trim();
            if (!BranchValidation.IsValidTimezone(timezone))
                return Results.BadRequest(new { error = "timezone inválida: debe ser un identificador IANA reconocido" });

            var created = await branches.CreateBranchAsync(businessId, code, request.Name.Trim(), timezone, ct);
            return created is null
                ? Results.Conflict(new { error = "Ya existe una sucursal con ese código" })
                : Results.Created($"/api/web/branches/{created.Code}", created);
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

        // ── Vinculación de dispositivo (Branch → ConnectorInstallation) ─────────────────────
        // El vínculo real lo hace la GUI del agente (extractor-ui), no el navegador: estos
        // endpoints se llaman desde ahí con la sesión de usuario recién iniciada, igual que
        // dashboard-web. Ver extractor-ui/LinkDeviceForm.cs.

        group.MapGet("/branches/{branchCode}/connector", async (
            HttpContext context,
            string branchCode,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();

            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            if (branch is null) return Results.NotFound();
            if (await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct) is null) return Results.NotFound();

            return Results.Ok(await installations.GetActiveInstallationAsync(branch.Id, ct));
        });

        group.MapGet("/branches/{branchCode}/connector-installations", async (
            HttpContext context,
            string branchCode,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();

            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            if (branch is null) return Results.NotFound();
            if (await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct) is null) return Results.NotFound();

            var history = await installations.GetInstallationsAsync(branchCode, ct);
            return history is null ? Results.NotFound() : Results.Ok(history);
        });

        group.MapPost("/branches/{branchCode}/link-device", async (
            HttpContext context,
            string branchCode,
            LinkDeviceRequest request,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            if (string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Length > 200)
                return Results.BadRequest(new { error = "machineName es obligatorio y admite máximo 200 caracteres" });

            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();

            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            if (branch is null || !branch.Active) return Results.NotFound();
            var role = await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct);
            if (role is null) return Results.NotFound();
            if (!BusinessAccess.CanManageBusiness(role))
                return Results.Json(new { error = "Este rol no puede vincular equipos." }, statusCode: StatusCodes.Status403Forbidden);

            var result = await installations.LinkDeviceAsync(branch.Id, user.Id, request, apiUrl: null, context, ct);
            return result.Status switch
            {
                LinkDeviceStatus.Ok => Results.Ok(result.Credential),
                LinkDeviceStatus.AlreadyActive => Results.Conflict(new
                {
                    error = "Esta sucursal ya tiene un conector activo. Use 'Reemplazar equipo' para continuar.",
                    activeInstallation = result.ActiveInstallation
                }),
                _ => Results.NotFound(),
            };
        }).RequireRateLimiting("dashboard-login");

        group.MapPost("/branches/{branchCode}/replace-device", async (
            HttpContext context,
            string branchCode,
            LinkDeviceRequest request,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            if (string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Length > 200)
                return Results.BadRequest(new { error = "machineName es obligatorio y admite máximo 200 caracteres" });

            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();

            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            if (branch is null || !branch.Active) return Results.NotFound();
            var role = await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct);
            if (role is null) return Results.NotFound();
            if (!BusinessAccess.CanManageBusiness(role))
                return Results.Json(new { error = "Este rol no puede reemplazar equipos." }, statusCode: StatusCodes.Status403Forbidden);

            var credential = await installations.ReplaceDeviceAsync(branch.Id, user.Id, request, apiUrl: null, context, ct);
            return Results.Ok(credential);
        }).RequireRateLimiting("dashboard-login");

        group.MapPost("/connector-installations/{installationId:guid}/revoke", async (
            HttpContext context,
            Guid installationId,
            string branchCode,
            WebAuthService auth,
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
            ConnectorInstallationRegistry installations,
            CancellationToken ct) =>
        {
            var validation = ValidateBranchCode(branchCode);
            if (validation is not null) return validation;
            var user = await auth.AuthenticateAsync(context, ct);
            if (user is null) return Results.Unauthorized();

            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            if (branch is null) return Results.NotFound();
            var role = await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct);
            if (role is null) return Results.NotFound();
            if (!BusinessAccess.CanManageBusiness(role))
                return Results.Json(new { error = "Este rol no puede revocar equipos." }, statusCode: StatusCodes.Status403Forbidden);

            return await installations.RevokeForBranchAsync(installationId, branch.Id, ct)
                ? Results.Ok(new { installationId, active = false })
                : Results.NotFound();
        });

        // ── Instalador ───────────────────────────────────────────────────────────────────────

        group.MapGet("/agent/latest", (ApiOptions options, HttpContext context) =>
        {
            // Requiere sesión igual que el resto de /api/web/*, pero no depende de una
            // sucursal concreta: cualquier cuenta autenticada puede descargar el instalador
            // universal actual. La arquitectura queda lista para más adelante reportar versión
            // instalada vs. disponible por sucursal (ver ConnectorInstallationView.AgentVersion).
            return Results.Ok(new { version = options.InstallerVersion, downloadUrl = options.InstallerDownloadUrl });
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
            BusinessRegistry businesses,
            BranchRegistry branchRegistry,
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
            var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
            var role = branch is null ? null : await businesses.GetMemberRoleAsync(branch.BusinessId, user.Id, ct);
            if (role is "VIEWER")
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
