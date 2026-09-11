using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record ExpenseCategoryRule(string Keyword, int Priority);
internal sealed record ExpenseCategory(Guid Id, string Name, int DisplayOrder, IReadOnlyList<ExpenseCategoryRule> Rules);
internal sealed record ExpenseCategoryRuleInput(string? Keyword, int? Priority);
internal sealed record ExpenseCategoryInput(string? Name, int? DisplayOrder, IReadOnlyList<ExpenseCategoryRuleInput>? Rules);

/// <summary>Configuración por negocio de la clasificación de salidas de caja.</summary>
internal sealed class ExpenseCategoryService(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<ExpenseCategory>> GetAsync(Guid businessId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT c.id, c.name, c.display_order, k.keyword, k.priority
            FROM expense_categories c
            LEFT JOIN expense_category_keywords k ON k.category_id = c.id
            WHERE c.business_id = $1 AND c.active
            ORDER BY c.display_order, c.name, k.priority, k.keyword;
            """);
        command.Parameters.AddWithValue(businessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var categories = new Dictionary<Guid, (string Name, int DisplayOrder, List<ExpenseCategoryRule> Rules)>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!categories.TryGetValue(id, out var category))
                categories[id] = category = (reader.GetString(1), reader.GetInt32(2), []);
            if (!reader.IsDBNull(3)) category.Rules.Add(new ExpenseCategoryRule(reader.GetString(3), reader.GetInt32(4)));
        }
        return categories.Select(pair => new ExpenseCategory(pair.Key, pair.Value.Name, pair.Value.DisplayOrder, pair.Value.Rules)).ToList();
    }

    public async Task<ExpenseCategory> CreateAsync(Guid businessId, ExpenseCategoryInput input, CancellationToken ct)
    {
        var (name, displayOrder, rules) = Validate(input);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO expense_categories (business_id, name, display_order)
            VALUES ($1, $2, $3)
            RETURNING id, display_order;
            """;
        command.Parameters.AddWithValue(businessId);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(displayOrder);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetGuid(0);
        var order = reader.GetInt32(1);
        await reader.CloseAsync();
        await InsertRulesAsync(connection, transaction, id, rules, ct);
        await transaction.CommitAsync(ct);
        await ReclassifyAutomaticAsync(businessId, ct);
        return new ExpenseCategory(id, name, order, rules);
    }

    public async Task<ExpenseCategory?> UpdateAsync(Guid businessId, Guid categoryId, ExpenseCategoryInput input, CancellationToken ct)
    {
        var (name, displayOrder, rules) = Validate(input);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE expense_categories SET name = $1, display_order = $2, updated_at = now() WHERE id = $3 AND business_id = $4 RETURNING display_order;";
        update.Parameters.AddWithValue(name);
        update.Parameters.AddWithValue(displayOrder);
        update.Parameters.AddWithValue(categoryId);
        update.Parameters.AddWithValue(businessId);
        var order = await update.ExecuteScalarAsync(ct);
        if (order is null) return null;
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM expense_category_keywords WHERE category_id = $1;";
            delete.Parameters.AddWithValue(categoryId);
            await delete.ExecuteNonQueryAsync(ct);
        }
        await InsertRulesAsync(connection, transaction, categoryId, rules, ct);
        await transaction.CommitAsync(ct);
        await ReclassifyAutomaticAsync(businessId, ct);
        return new ExpenseCategory(categoryId, name, Convert.ToInt32(order), rules);
    }

    public async Task<bool> DeleteAsync(Guid businessId, Guid categoryId, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var resetManual = connection.CreateCommand())
        {
            resetManual.Transaction = transaction;
            resetManual.CommandText = "UPDATE cash_movements SET expense_category_id = NULL, expense_category_source = NULL WHERE expense_category_id = $1 AND expense_category_source = 'MANUAL';";
            resetManual.Parameters.AddWithValue(categoryId);
            await resetManual.ExecuteNonQueryAsync(ct);
        }
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM expense_categories WHERE id = $1 AND business_id = $2;";
        command.Parameters.AddWithValue(categoryId);
        command.Parameters.AddWithValue(businessId);
        var deleted = await command.ExecuteNonQueryAsync(ct) == 1;
        await transaction.CommitAsync(ct);
        if (deleted) await ReclassifyAutomaticAsync(businessId, ct);
        return deleted;
    }

    public async Task<bool> SetManualCategoryAsync(Guid branchId, Guid businessId, string idempotencyKey, Guid categoryId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE cash_movements cm
            SET expense_category_id = $3, expense_category_source = 'MANUAL', updated_at = now()
            WHERE cm.branch_id = $1 AND cm.idempotency_key = $2 AND cm.movement_type = 1
              AND EXISTS (SELECT 1 FROM expense_categories c WHERE c.id = $3 AND c.business_id = $4 AND c.active);
            """);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(idempotencyKey);
        command.Parameters.AddWithValue(categoryId);
        command.Parameters.AddWithValue(businessId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> ResetToAutomaticAsync(Guid branchId, Guid businessId, string idempotencyKey, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE cash_movements cm SET expense_category_source = NULL
            FROM branches b
            WHERE cm.branch_id = $1 AND cm.idempotency_key = $2 AND cm.branch_id = b.id AND b.business_id = $3;
            """);
        command.Parameters.AddWithValue(branchId);
        command.Parameters.AddWithValue(idempotencyKey);
        command.Parameters.AddWithValue(businessId);
        if (await command.ExecuteNonQueryAsync(ct) != 1) return false;
        await ReclassifyAutomaticAsync(businessId, idempotencyKey, ct);
        return true;
    }

    public async Task ReclassifyAutomaticAsync(Guid businessId, CancellationToken ct) =>
        await ReclassifyAutomaticAsync(businessId, null, ct);

    private async Task ReclassifyAutomaticAsync(Guid businessId, string? idempotencyKey, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            WITH classified AS (
                SELECT cm.branch_id, cm.idempotency_key, matched.category_id
                FROM cash_movements cm
                JOIN branches b ON b.id = cm.branch_id
                LEFT JOIN LATERAL (
                    SELECT r.category_id
                    FROM expense_category_keywords r
                    JOIN expense_categories c ON c.id = r.category_id
                    WHERE c.business_id = b.business_id AND c.active
                      AND COALESCE(cm.payload->>'concepto', '') ILIKE '%' || r.keyword || '%'
                    ORDER BY r.priority, c.display_order, length(r.keyword) DESC, c.name
                    LIMIT 1
                ) matched ON true
                WHERE b.business_id = $1
                  AND cm.movement_type = 1 AND NOT cm.cancelled
                  AND cm.expense_category_source IS DISTINCT FROM 'MANUAL'
                  AND ($2::text IS NULL OR cm.idempotency_key = $2)
            )
            UPDATE cash_movements cm
            SET expense_category_id = classified.category_id,
                expense_category_source = CASE WHEN classified.category_id IS NULL THEN NULL ELSE 'AUTOMATIC' END,
                updated_at = now()
            FROM classified
            WHERE cm.branch_id = classified.branch_id AND cm.idempotency_key = classified.idempotency_key;
            """);
        command.Parameters.AddWithValue(businessId);
        command.Parameters.AddWithValue((object?)idempotencyKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static (string Name, int DisplayOrder, IReadOnlyList<ExpenseCategoryRule> Rules) Validate(ExpenseCategoryInput input)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80) throw new ArgumentException("El nombre de categoría debe tener entre 2 y 80 caracteres.");
        var displayOrder = Math.Clamp(input.DisplayOrder ?? 100, 1, 10000);
        var rules = (input.Rules ?? [])
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Keyword))
            .Select(rule => new ExpenseCategoryRule(rule.Keyword!.Trim(), Math.Clamp(rule.Priority ?? 100, 1, 10000)))
            .GroupBy(rule => rule.Keyword, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(rule => rule.Priority).First())
            .ToArray();
        if (rules.Length == 0 || rules.Length > 30 || rules.Any(rule => rule.Keyword.Length > 100))
            throw new ArgumentException("Agrega de 1 a 30 palabras clave de máximo 100 caracteres.");
        return (name, displayOrder, rules);
    }

    private static async Task InsertRulesAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid categoryId, IReadOnlyList<ExpenseCategoryRule> rules, CancellationToken ct)
    {
        foreach (var rule in rules)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO expense_category_keywords (category_id, keyword, priority) VALUES ($1, $2, $3);";
            command.Parameters.AddWithValue(categoryId);
            command.Parameters.AddWithValue(rule.Keyword);
            command.Parameters.AddWithValue(rule.Priority);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
