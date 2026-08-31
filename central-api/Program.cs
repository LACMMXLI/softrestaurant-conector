using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using SoftRestaurant.CentralApi;
using SoftRestaurant.Sync.Contracts;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512L * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 512L * 1024 * 1024);

var apiOptions = ApiOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(apiOptions);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(apiOptions.ConnectionString));
builder.Services.AddSingleton<BatchIngestor>();
builder.Services.AddSingleton<BranchRegistry>();
builder.Services.AddSingleton<BusinessRegistry>();
builder.Services.AddSingleton<ConnectorInstallationRegistry>();
builder.Services.AddSingleton<WebAuthService>();
builder.Services.AddSingleton<UserRegistry>();
builder.Services.AddSingleton<DashboardReportService>();
builder.Services.AddRateLimiter(options =>
{
    // Misma política para login, autorregistro y emisión de credenciales de dispositivo
    // (link-device/replace-device): todas producen un token utilizable, así que todas merecen
    // el mismo límite de fuerza bruta por IP.
    options.AddPolicy("dashboard-login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};
// En Coolify la API solo se expone a través del contenedor web. La subred Docker es dinámica,
// por eso se acepta exactamente un salto de proxy para recuperar HTTPS e IP del cliente.
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeaders);
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/web"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
if (apiOptions.ConnectorAdminKey.Length == 0)
    app.Logger.LogWarning("CONNECTOR_ADMIN_KEY no está configurado; los endpoints administrativos quedan deshabilitados.");
var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
await DbInitializer.InitializeAsync(dataSource, apiOptions, CancellationToken.None);
await app.Services.GetRequiredService<WebAuthService>()
    .EnsureBootstrapOwnerAsync(CancellationToken.None);

app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/health/ready", async (CancellationToken ct) =>
{
    await using var command = dataSource.CreateCommand("SELECT 1;");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ok", database = "up" });
});

// ── /api/admin/businesses — operador (SUPERADMIN / X-Admin-Key) ─────────────────────────────

app.MapGet("/api/admin/businesses", async (
    HttpContext context,
    BusinessRegistry businesses,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return Results.Ok(await businesses.GetAllBusinessesAsync(ct));
});

// ── /api/admin/branches — operador ───────────────────────────────────────────────────────────

app.MapGet("/api/admin/branches", async (
    HttpContext context,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return Results.Ok(await registry.GetAllBranchesAsync(ct));
});

app.MapGet("/api/admin/branches/{branchCode}", async (
    HttpContext context,
    string branchCode,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (!BranchValidation.IsValidCode(branchCode))
        return Results.BadRequest(new { error = "branchCode inválido" });

    var branch = await registry.GetBranchAsync(branchCode, ct);
    return branch is null ? Results.NotFound() : Results.Ok(branch);
});

app.MapPost("/api/admin/branches", async (
    HttpContext context,
    BranchCreateRequest request,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (request.BusinessId == Guid.Empty)
        return Results.BadRequest(new { error = "businessId es obligatorio" });
    var code = request.Code?.Trim() ?? string.Empty;
    if (!BranchValidation.IsValidCode(code))
        return Results.BadRequest(new { error = "code es obligatorio: minúsculas, dígitos y guiones, 2 a 63 caracteres" });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > BranchValidation.MaxNameLength)
        return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });
    var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "America/Tijuana" : request.Timezone.Trim();
    if (!BranchValidation.IsValidTimezone(timezone))
        return Results.BadRequest(new { error = "timezone inválida: debe ser un identificador IANA reconocido" });

    var created = await registry.CreateBranchAsync(request.BusinessId, code, request.Name.Trim(), timezone, ct);
    return created is null
        ? Results.Conflict(new { error = "Ya existe una sucursal con ese código" })
        : Results.Created($"/api/admin/branches/{created.Code}", created);
});

app.MapPut("/api/admin/branches/{branchCode}", async (
    HttpContext context,
    string branchCode,
    BranchUpdateRequest request,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (!BranchValidation.IsValidCode(branchCode))
        return Results.BadRequest(new { error = "branchCode inválido" });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > BranchValidation.MaxNameLength)
        return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });
    var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "America/Tijuana" : request.Timezone.Trim();
    if (!BranchValidation.IsValidTimezone(timezone))
        return Results.BadRequest(new { error = "timezone inválida: debe ser un identificador IANA reconocido" });

    var updated = await registry.UpdateBranchAsync(branchCode, request.Name.Trim(), timezone, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapPost("/api/admin/branches/{branchCode}/status", async (
    HttpContext context,
    string branchCode,
    BranchStatusRequest request,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (!BranchValidation.IsValidCode(branchCode))
        return Results.BadRequest(new { error = "branchCode inválido" });

    // No se elimina físicamente ninguna sucursal: solo se conmuta `active`. El historial
    // (ventas, turnos, movimientos de caja, etc.) permanece intacto y consultable.
    var updated = await registry.SetBranchActiveAsync(branchCode, request.Active, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

// ── /api/admin/users — operador ──────────────────────────────────────────────────────────────

app.MapGet("/api/admin/users", async (
    HttpContext context,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return Results.Ok(await users.GetAllUsersAsync(ct));
});

app.MapGet("/api/admin/users/{id:guid}", async (
    HttpContext context,
    Guid id,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var user = await users.GetUserAsync(id, ct);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

app.MapPost("/api/admin/users", async (
    HttpContext context,
    UserCreateRequest request,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var email = request.Email?.Trim() ?? string.Empty;
    if (!UserValidation.IsValidEmail(email))
        return Results.BadRequest(new { error = "email es obligatorio y debe tener formato válido" });
    if (!UserValidation.IsValidDisplayName(request.DisplayName))
        return Results.BadRequest(new { error = $"displayName es obligatorio y admite máximo {UserValidation.MaxDisplayNameLength} caracteres" });
    if (!UserValidation.IsValidPassword(request.Password))
        return Results.BadRequest(new { error = $"password debe tener al menos {UserValidation.MinPasswordLength} caracteres" });
    if (!UserValidation.IsValidAccountRole(request.Role))
        return Results.BadRequest(new { error = "role inválido: use SUPERADMIN o USER" });

    var created = await users.CreateUserAsync(email, request.DisplayName.Trim(), request.Password, request.Role, ct);
    return created is null
        ? Results.Conflict(new { error = "Ya existe una cuenta con ese correo" })
        : Results.Created($"/api/admin/users/{created.Id}", created);
});

app.MapPut("/api/admin/users/{id:guid}", async (
    HttpContext context,
    Guid id,
    UserUpdateRequest request,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    var principal = await AdminAuthenticator.AuthorizeAsync(context, apiOptions, webAuth, ct);
    if (principal is null) return Results.Unauthorized();
    if (!UserValidation.IsValidDisplayName(request.DisplayName))
        return Results.BadRequest(new { error = $"displayName es obligatorio y admite máximo {UserValidation.MaxDisplayNameLength} caracteres" });
    if (!UserValidation.IsValidAccountRole(request.Role))
        return Results.BadRequest(new { error = "role inválido: use SUPERADMIN o USER" });

    var result = await users.UpdateUserAsync(id, request.DisplayName.Trim(), request.Role, ct);
    return result.Status switch
    {
        UserMutationStatus.NotFound => Results.NotFound(),
        UserMutationStatus.BlockedLastSuperAdmin => Results.Conflict(new
        {
            error = "No puede quitar el rol SUPERADMIN a la última cuenta SUPERADMIN activa."
        }),
        _ => Results.Ok(new { user = result.User, selfAffected = principal.User?.Id == id }),
    };
});

app.MapPost("/api/admin/users/{id:guid}/status", async (
    HttpContext context,
    Guid id,
    UserStatusRequest request,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    var principal = await AdminAuthenticator.AuthorizeAsync(context, apiOptions, webAuth, ct);
    if (principal is null) return Results.Unauthorized();

    // No se elimina físicamente ninguna cuenta: solo se conmuta `active`, igual que en
    // sucursales. Conserva el historial de auditoría (audit_log, last_login_at).
    var result = await users.SetUserActiveAsync(id, request.Active, ct);
    return result.Status switch
    {
        UserMutationStatus.NotFound => Results.NotFound(),
        UserMutationStatus.BlockedLastSuperAdmin => Results.Conflict(new
        {
            error = "No puede desactivar a la última cuenta SUPERADMIN activa."
        }),
        // selfAffected: la propia sesión del que llama ya fue revocada si se desactivó a sí
        // mismo (SetUserActiveAsync invalida sus sesiones); el panel debe mostrarlo con
        // claridad y no seguir tratando esta respuesta como una sesión válida.
        _ => Results.Ok(new { user = result.User, selfAffected = principal.User?.Id == id }),
    };
});

app.MapPost("/api/admin/users/{id:guid}/password", async (
    HttpContext context,
    Guid id,
    UserPasswordResetRequest request,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    var principal = await AdminAuthenticator.AuthorizeAsync(context, apiOptions, webAuth, ct);
    if (principal is null) return Results.Unauthorized();
    if (!UserValidation.IsValidPassword(request.Password))
        return Results.BadRequest(new { error = $"password debe tener al menos {UserValidation.MinPasswordLength} caracteres" });

    var updated = await users.ResetPasswordAsync(id, request.Password, ct);
    return updated is null
        ? Results.NotFound()
        : Results.Ok(new { user = updated, selfAffected = principal.User?.Id == id });
});

app.MapPost("/api/admin/users/{id:guid}/businesses", async (
    HttpContext context,
    Guid id,
    UserBusinessAssignRequest request,
    UserRegistry users,
    BusinessRegistry businesses,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (request.BusinessIds.Count == 0)
        return Results.BadRequest(new { error = "businessIds no puede estar vacío" });
    if (!UserValidation.IsValidBusinessRole(request.Role))
        return Results.BadRequest(new { error = "role inválido: use OWNER, MANAGER o VIEWER" });

    var updated = await users.AssignBusinessesAsync(id, request.BusinessIds, request.Role, businesses, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/admin/users/{id:guid}/businesses/{businessId:guid}", async (
    HttpContext context,
    Guid id,
    Guid businessId,
    UserRegistry users,
    BusinessRegistry businesses,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return await users.RemoveBusinessAsync(id, businessId, businesses, ct)
        ? Results.Ok(new { id, businessId, removed = true })
        : Results.NotFound();
});

// ── /api/admin/branches/{branchCode}/connector-installations — operador ────────────────────

app.MapGet("/api/admin/branches/{branchCode}/connector-installations", async (
    HttpContext context,
    string branchCode,
    ConnectorInstallationRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var installations = await registry.GetInstallationsAsync(branchCode, ct);
    return installations is null ? Results.NotFound() : Results.Ok(installations);
});

app.MapPost("/api/admin/connector-installations/{installationId:guid}/revoke", async (
    HttpContext context,
    Guid installationId,
    ConnectorInstallationRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return await registry.RevokeAsync(installationId, ct)
        ? Results.Ok(new { installationId, active = false })
        : Results.NotFound();
});

app.MapPost("/api/admin/branches/{branchCode}/replace-device", async (
    HttpContext context,
    string branchCode,
    LinkDeviceRequest request,
    BranchRegistry branchRegistry,
    ConnectorInstallationRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    // Soporte: un SUPERADMIN puede reemplazar el equipo de una sucursal en nombre de un tenant
    // que no puede llegar a la GUI del agente (perdió acceso, sin nadie en sitio, etc.).
    var principal = await AdminAuthenticator.AuthorizeAsync(context, apiOptions, webAuth, ct);
    if (principal is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Length > 200)
        return Results.BadRequest(new { error = "machineName es obligatorio y admite máximo 200 caracteres" });

    var branch = await branchRegistry.GetBranchAsync(branchCode, ct);
    if (branch is null) return Results.NotFound();

    var credential = await registry.ReplaceDeviceAsync(
        branch.Id, principal.User?.Id ?? Guid.Empty, request, apiUrl: null, context, ct);
    return Results.Ok(credential);
});

// ── /api/ingestion, /api/dashboard, /api/agents, /api/branches — agente (identidad de dispositivo) ──

app.MapPost("/api/ingestion/batches", async (
    HttpContext context,
    SyncBatch batch,
    BatchIngestor ingestor,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(batch.BatchId) || string.IsNullOrWhiteSpace(batch.BranchCode))
        return Results.BadRequest(new { error = "batchId y branchCode son obligatorios" });
    if (batch.RangeEnd <= batch.RangeStart)
        return Results.BadRequest(new { error = "El rango es inválido" });
    if (!batch.ReconciliationOk || batch.Reconciliation.Any(x => !x.Match))
        return Results.UnprocessableEntity(new { error = "El lote no está conciliado con SoftRestaurant" });

    var identity = await AgentAuthenticator.AuthenticateAsync(context, dataSource, batch.BranchCode, ct);
    if (identity is null) return Results.Unauthorized();

    await ingestor.IngestAsync(identity.BranchId, identity.ConnectorInstallationId, batch, ct);
    return Results.Ok(new
    {
        accepted = true,
        batchId = batch.BatchId,
        counts = new
        {
            sales = batch.Sales.Count,
            lines = batch.Lines.Count,
            payments = batch.Payments.Count,
            shifts = batch.Shifts.Count,
            cashMovements = batch.CashMovements.Count,
            cancellations = batch.Cancellations.Count
        }
    });
}).DisableAntiforgery();

app.MapGet("/api/dashboard/today", async (
    HttpContext context,
    string branchCode,
    DateOnly? date,
    CancellationToken ct) =>
{
    var identity = await AgentAuthenticator.AuthenticateAsync(context, dataSource, branchCode, ct);
    if (identity is null) return Results.Unauthorized();

    var selectedDate = date ?? DateOnly.FromDateTime(DateTime.Today);
    await using var connection = await dataSource.OpenConnectionAsync(ct);
    await using var command = new NpgsqlCommand("""
        SELECT
            COUNT(*) FILTER (WHERE paid AND NOT cancelled AND closed_at IS NOT NULL) AS tickets,
            COALESCE(SUM(total) FILTER (WHERE paid AND NOT cancelled AND closed_at IS NOT NULL), 0) AS sales,
            COUNT(*) FILTER (WHERE cancelled) AS cancelled_tickets,
            COALESCE(AVG(total) FILTER (WHERE paid AND NOT cancelled AND closed_at IS NOT NULL), 0) AS average_ticket,
            COALESCE((
                SELECT SUM(cm.amount)
                FROM cash_movements cm
                WHERE cm.branch_id = $1 AND cm.movement_type = 1 AND NOT cm.cancelled
                  AND cm.movement_date >= $2::date AND cm.movement_date < $2::date + 1
            ), 0) AS cash_out,
            COALESCE((
                SELECT SUM(cs.occurrences)
                FROM cancellation_summaries cs
                WHERE cs.branch_id = $1 AND cs.cancellation_date = $2::date
            ), 0) AS cancelled_lines
        FROM sales s
        WHERE s.branch_id = $1
          AND s.business_date >= $2::date
          AND s.business_date < $2::date + 1;
        """, connection);
    command.Parameters.AddWithValue(identity.BranchId);
    command.Parameters.AddWithValue(selectedDate.ToDateTime(TimeOnly.MinValue));
    await using var reader = await command.ExecuteReaderAsync(ct);
    await reader.ReadAsync(ct);
    var summary = new
    {
        branchCode,
        date = selectedDate,
        tickets = reader.GetInt64(0),
        sales = reader.GetDecimal(1),
        cancelledTickets = reader.GetInt64(2),
        averageTicket = reader.GetDecimal(3),
        cashOut = reader.GetDecimal(4),
        cancelledLines = reader.GetInt64(5)
    };
    return Results.Ok(summary);
});

app.MapPost("/api/agents/heartbeat", async (
    HttpContext context,
    HeartbeatRequest request,
    ConnectorInstallationRegistry registry,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.BranchCode) || !BranchValidation.IsValidCode(request.BranchCode))
        return Results.BadRequest(new { error = "branchCode inválido" });
    if (request.State?.Length > 20)
        return Results.BadRequest(new { error = "state demasiado largo" });
    if (request.LastError?.Length > 2000)
        return Results.BadRequest(new { error = "lastError admite máximo 2000 caracteres" });

    var identity = await AgentAuthenticator.AuthenticateAsync(context, dataSource, request.BranchCode, ct);
    if (identity is null) return Results.Unauthorized();

    var result = await registry.RecordHeartbeatAsync(
        identity.ConnectorInstallationId, identity.BranchId, request.State ?? "Idle", request.LastError,
        Math.Max(0, request.PendingBatches), request.LastSyncRequestHandledAt, ct);

    return Results.Ok(new { syncRequestedAt = result.SyncRequestedAt, serverTimeUtc = DateTime.UtcNow });
});

app.MapPost("/api/admin/branches/{branchCode}/request-sync", async (
    HttpContext context,
    string branchCode,
    BranchRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    var principal = await AdminAuthenticator.AuthorizeAsync(context, apiOptions, webAuth, ct);
    if (principal is null) return Results.Unauthorized();
    if (!BranchValidation.IsValidCode(branchCode))
        return Results.BadRequest(new { error = "branchCode inválido" });

    var updated = await registry.RequestSyncAsync(branchCode, principal.User?.Id, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapGet("/api/branches/{branchCode}/sync-status", async (
    HttpContext context,
    string branchCode,
    CancellationToken ct) =>
{
    var identity = await AgentAuthenticator.AuthenticateAsync(context, dataSource, branchCode, ct);
    if (identity is null) return Results.Unauthorized();

    await using var command = dataSource.CreateCommand("""
        SELECT b.last_sync_at, sb.id, sb.range_start, sb.range_end, sb.reconciliation_ok, sb.counts
        FROM branches b
        LEFT JOIN LATERAL (
            SELECT id, range_start, range_end, reconciliation_ok, counts
            FROM sync_batches
            WHERE branch_id = b.id
            ORDER BY received_at DESC
            LIMIT 1
        ) sb ON true
        WHERE b.id = $1;
        """);
    command.Parameters.AddWithValue(identity.BranchId);
    await using var reader = await command.ExecuteReaderAsync(ct);
    if (!await reader.ReadAsync(ct)) return Results.NotFound();
    return Results.Ok(new
    {
        branchCode,
        lastSyncAt = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
        lastBatchId = reader.IsDBNull(1) ? null : reader.GetString(1),
        rangeStart = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
        rangeEnd = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3),
        reconciliationOk = reader.IsDBNull(4) ? (bool?)null : reader.GetBoolean(4),
        counts = reader.IsDBNull(5) ? null : reader.GetFieldValue<System.Text.Json.JsonDocument>(5)
    });
});

app.MapDashboardWebApi();

app.Run();

internal sealed record HeartbeatRequest(
    string BranchCode,
    string AgentVersion,
    string? State,
    DateTime? LastSuccessAt,
    string? LastError,
    int PendingBatches,
    DateTime? LastSyncRequestHandledAt);
