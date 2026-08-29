using Microsoft.AspNetCore.Http.Features;
using Npgsql;
using SoftRestaurant.CentralApi;
using SoftRestaurant.Sync.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512L * 1024 * 1024);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 512L * 1024 * 1024);

var apiOptions = ApiOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(apiOptions);
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(apiOptions.ConnectionString));
builder.Services.AddSingleton<BatchIngestor>();
builder.Services.AddSingleton<ConnectorRegistry>();

var app = builder.Build();
if (apiOptions.ConnectorAdminKey.Length == 0)
    app.Logger.LogWarning("CONNECTOR_ADMIN_KEY no está configurado; los endpoints administrativos quedan deshabilitados.");
var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();
await DbInitializer.InitializeAsync(dataSource, apiOptions, CancellationToken.None);

app.MapGet("/api/health/live", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/health/ready", async (CancellationToken ct) =>
{
    await using var command = dataSource.CreateCommand("SELECT 1;");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ok", database = "up" });
});

app.MapPut("/api/admin/branches/{branchCode}", async (
    HttpContext context,
    string branchCode,
    BranchRequest request,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
    if (!System.Text.RegularExpressions.Regex.IsMatch(branchCode, "^[a-z0-9][a-z0-9-]{1,62}$"))
        return Results.BadRequest(new { error = "branchCode inválido" });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        return Results.BadRequest(new { error = "name es obligatorio y admite máximo 200 caracteres" });
    var timezone = string.IsNullOrWhiteSpace(request.Timezone) ? "America/Tijuana" : request.Timezone.Trim();
    if (timezone.Length > 100) return Results.BadRequest(new { error = "timezone demasiado largo" });

    return Results.Ok(await registry.UpsertBranchAsync(branchCode, request.Name.Trim(), timezone, ct));
});

app.MapPost("/api/admin/branches/{branchCode}/activation-keys", async (
    HttpContext context,
    string branchCode,
    ActivationKeyRequest request,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
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
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
    var connectors = await registry.GetConnectorsAsync(branchCode, ct);
    return connectors is null ? Results.NotFound() : Results.Ok(connectors);
});

app.MapPost("/api/admin/connectors/{connectorId:guid}/revoke", async (
    HttpContext context,
    Guid connectorId,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
    return await registry.RevokeAsync(connectorId, ct)
        ? Results.Ok(new { connectorId, active = false })
        : Results.NotFound();
});

app.MapPost("/api/admin/connectors/{connectorId:guid}/rotate-token", async (
    HttpContext context,
    Guid connectorId,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
    var credential = await registry.RotateTokenAsync(connectorId, ct);
    return credential is null
        ? Results.NotFound()
        : Results.Ok(credential);
});

app.MapPost("/api/admin/branches/{branchCode}/legacy-auth/disable", async (
    HttpContext context,
    string branchCode,
    ConnectorRegistry registry,
    CancellationToken ct) =>
{
    if (!AdminAuthenticator.IsAuthorized(context, apiOptions)) return Results.Unauthorized();
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

app.Run();
