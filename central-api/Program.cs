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
builder.Services.AddSingleton<ConnectorRegistry>();
builder.Services.AddSingleton<WebAuthService>();
builder.Services.AddSingleton<UserRegistry>();
builder.Services.AddSingleton<DashboardReportService>();
builder.Services.AddRateLimiter(options =>
{
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

app.MapGet("/api/admin/branches", async (
    HttpContext context,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return Results.Ok(await registry.GetAllBranchesAsync(ct));
});

app.MapGet("/api/admin/branches/{branchCode}", async (
    HttpContext context,
    string branchCode,
    ConnectorRegistry registry,
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
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var code = request.Code?.Trim() ?? string.Empty;
    if (!BranchValidation.IsValidCode(code))
        return Results.BadRequest(new { error = "code es obligatorio: minúsculas, dígitos y guiones, 2 a 63 caracteres" });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > BranchValidation.MaxNameLength)
        return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });
    var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "America/Tijuana" : request.Timezone.Trim();
    if (!BranchValidation.IsValidTimezone(timezone))
        return Results.BadRequest(new { error = "timezone inválida: debe ser un identificador IANA reconocido" });

    var created = await registry.CreateBranchAsync(code, request.Name.Trim(), timezone, ct);
    return created is null
        ? Results.Conflict(new { error = "Ya existe una sucursal con ese código" })
        : Results.Created($"/api/admin/branches/{created.Code}", created);
});

app.MapPut("/api/admin/branches/{branchCode}", async (
    HttpContext context,
    string branchCode,
    BranchUpdateRequest request,
    ConnectorRegistry registry,
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
    ConnectorRegistry registry,
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
    if (!UserValidation.IsValidRole(request.Role))
        return Results.BadRequest(new { error = "role inválido: use SUPERADMIN, OWNER, MANAGER o VIEWER" });
    if (request.BranchCodes is { Count: > 0 } createCodes && createCodes.Any(c => !BranchValidation.IsValidCode(c)))
        return Results.BadRequest(new { error = "branchCodes contiene un código de sucursal inválido" });

    var created = await users.CreateUserAsync(
        email, request.DisplayName.Trim(), request.Password, request.Role, request.BranchCodes, ct);
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
    if (!UserValidation.IsValidRole(request.Role))
        return Results.BadRequest(new { error = "role inválido: use SUPERADMIN, OWNER, MANAGER o VIEWER" });

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

app.MapPost("/api/admin/users/{id:guid}/branches", async (
    HttpContext context,
    Guid id,
    UserBranchAssignRequest request,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    if (request.BranchCodes.Any(c => !BranchValidation.IsValidCode(c)))
        return Results.BadRequest(new { error = "branchCodes contiene un código de sucursal inválido" });

    var updated = await users.AssignBranchesAsync(id, request.BranchCodes, ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});

app.MapDelete("/api/admin/users/{id:guid}/branches/{branchCode}", async (
    HttpContext context,
    Guid id,
    string branchCode,
    UserRegistry users,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return await users.RemoveBranchAsync(id, branchCode, ct)
        ? Results.Ok(new { id, branchCode, removed = true })
        : Results.NotFound();
});

app.MapPost("/api/admin/branches/{branchCode}/activation-keys", async (
    HttpContext context,
    string branchCode,
    ActivationKeyRequest request,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var minutes = request.ExpiresInMinutes ?? 30;
    if (minutes is < 1 or > 10080)
        return Results.BadRequest(new { error = "expiresInMinutes debe estar entre 1 y 10080" });
    if (request.Note?.Length > 500)
        return Results.BadRequest(new { error = "note admite máximo 500 caracteres" });

    var created = await registry.CreateActivationKeyAsync(branchCode, minutes, request.Note, ct);
    return created is null
        ? Results.NotFound(new { error = "Sucursal inexistente o inactiva" })
        : Results.Created($"/api/admin/activation-keys/{created.Id}", created);
});

app.MapPost("/api/connectors/activate", async (
    HttpContext context,
    ActivateConnectorRequest request,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.ActivationKey) || request.ActivationKey.Length > 200)
        return Results.BadRequest(new { error = "activationKey inválida" });
    if (string.IsNullOrWhiteSpace(request.MachineName) || request.MachineName.Length > 200)
        return Results.BadRequest(new { error = "machineName es obligatorio y admite máximo 200 caracteres" });
    if (request.AgentVersion?.Length > 100)
        return Results.BadRequest(new { error = "agentVersion demasiado largo" });
    if ((request.Metadata?.GetRawText().Length ?? 0) > 8192)
        return Results.BadRequest(new { error = "metadata admite máximo 8 KB" });

    var activation = await registry.ActivateAsync(request with
    {
        ActivationKey = request.ActivationKey.Trim(),
        MachineName = request.MachineName.Trim()
    }, context, ct);
    return activation is null
        ? Results.Unauthorized()
        : Results.Ok(activation);
});

app.MapGet("/api/admin/branches/{branchCode}/connectors", async (
    HttpContext context,
    string branchCode,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var connectors = await registry.GetConnectorsAsync(branchCode, ct);
    return connectors is null ? Results.NotFound() : Results.Ok(connectors);
});

app.MapPost("/api/admin/connectors/{connectorId:guid}/revoke", async (
    HttpContext context,
    Guid connectorId,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return await registry.RevokeAsync(connectorId, ct)
        ? Results.Ok(new { connectorId, active = false })
        : Results.NotFound();
});

app.MapPost("/api/admin/connectors/{connectorId:guid}/rotate-token", async (
    HttpContext context,
    Guid connectorId,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    var credential = await registry.RotateTokenAsync(connectorId, ct);
    return credential is null
        ? Results.NotFound()
        : Results.Ok(credential);
});

app.MapPost("/api/admin/branches/{branchCode}/legacy-auth/disable", async (
    HttpContext context,
    string branchCode,
    ConnectorRegistry registry,
    WebAuthService webAuth,
    CancellationToken ct) =>
{
    if (!await AdminAuthenticator.IsAuthorizedAsync(context, apiOptions, webAuth, ct)) return Results.Unauthorized();
    return await registry.DisableLegacyAsync(branchCode, ct)
        ? Results.Ok(new { branchCode, legacyAuthEnabled = false })
        : Results.NotFound();
});

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

    await ingestor.IngestAsync(identity.BranchId, batch, ct);
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
