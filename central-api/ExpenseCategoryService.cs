using Npgsql;

namespace RestaurantAgent.CentralApi;

internal sealed record ExpenseCategory(Guid Id, string Name, int DisplayOrder, IReadOnlyList<string> Keywords);
internal sealed record ExpenseCategoryInput(string? Name, IReadOnlyList<string>? Keywords);

/// <summary>Configuración por negocio de la clasificación de salidas de caja.</summary>
internal sealed class ExpenseCategoryService(NpgsqlDataSource dataSource)
{
    public async Task<IReadOnlyList<ExpenseCategory>> GetAsync(Guid businessId, CancellationToken ct)
    {
        await EnsureDefaultsAsync(businessId, ct);
        await using var command = dataSource.CreateCommand("""
            SELECT c.id, c.name, c.display_order,
                   COALESCE(array_agg(k.keyword ORDER BY k.keyword) FILTER (WHERE k.keyword IS NOT NULL), ARRAY[]::text[])
            FROM expense_categories c
            LEFT JOIN expense_category_keywords k ON k.category_id = c.id
            WHERE c.business_id = $1 AND c.active
            GROUP BY c.id, c.name, c.display_order
            ORDER BY c.display_order, c.name;
            """);
        command.Parameters.AddWithValue(businessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var categories = new List<ExpenseCategory>();
        while (await reader.ReadAsync(ct))
            categories.Add(new ExpenseCategory(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetFieldValue<string[]>(3)));
        return categories;
    }

    public async Task<ExpenseCategory> CreateAsync(Guid businessId, ExpenseCategoryInput input, CancellationToken ct)
    {
        var (name, keywords) = Validate(input);
        await EnsureDefaultsAsync(businessId, ct);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO expense_categories (business_id, name, display_order)
            VALUES ($1, $2, COALESCE((SELECT MAX(display_order) + 1 FROM expense_categories WHERE business_id = $1), 1))
            RETURNING id, display_order;
            """;
        command.Parameters.AddWithValue(businessId);
        command.Parameters.AddWithValue(name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var id = reader.GetGuid(0);
        var order = reader.GetInt32(1);
        await reader.CloseAsync();
        await InsertKeywordsAsync(connection, transaction, id, keywords, ct);
        await transaction.CommitAsync(ct);
        return new ExpenseCategory(id, name, order, keywords);
    }

    public async Task<ExpenseCategory?> UpdateAsync(Guid businessId, Guid categoryId, ExpenseCategoryInput input, CancellationToken ct)
    {
        var (name, keywords) = Validate(input);
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE expense_categories SET name = $1, updated_at = now() WHERE id = $2 AND business_id = $3 RETURNING display_order;";
        update.Parameters.AddWithValue(name);
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
        await InsertKeywordsAsync(connection, transaction, categoryId, keywords, ct);
        await transaction.CommitAsync(ct);
        return new ExpenseCategory(categoryId, name, Convert.ToInt32(order), keywords);
    }

    public async Task<bool> DeleteAsync(Guid businessId, Guid categoryId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM expense_categories WHERE id = $1 AND business_id = $2;");
        command.Parameters.AddWithValue(categoryId);
        command.Parameters.AddWithValue(businessId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    private async Task EnsureDefaultsAsync(Guid businessId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            WITH inserted AS (
                INSERT INTO expense_categories (business_id, name, display_order, template_name)
                SELECT $1, t.name, t.display_order, t.name
                FROM expense_category_templates t
                WHERE NOT EXISTS (
                    SELECT 1 FROM expense_categories c WHERE c.business_id = $1 AND c.template_name = t.name)
                ON CONFLICT (business_id, name) DO NOTHING
                RETURNING id, template_name
            )
            INSERT INTO expense_category_keywords (category_id, keyword)
            SELECT inserted.id, k.keyword
            FROM inserted
            JOIN expense_category_keyword_templates k ON k.category_name = inserted.template_name
            ON CONFLICT (category_id, keyword) DO NOTHING;
            """);
        command.Parameters.AddWithValue(businessId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static (string Name, IReadOnlyList<string> Keywords) Validate(ExpenseCategoryInput input)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80) throw new ArgumentException("El nombre de categoría debe tener entre 2 y 80 caracteres.");
        var keywords = (input.Keywords ?? [])
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (keywords.Length == 0 || keywords.Length > 30 || keywords.Any(value => value.Length > 100))
            throw new ArgumentException("Agrega de 1 a 30 palabras clave de máximo 100 caracteres.");
        return (name, keywords);
    }

    private static async Task InsertKeywordsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid categoryId, IReadOnlyList<string> keywords, CancellationToken ct)
    {
        foreach (var keyword in keywords)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO expense_category_keywords (category_id, keyword) VALUES ($1, $2) ON CONFLICT (category_id, keyword) DO NOTHING;";
            command.Parameters.AddWithValue(categoryId);
            command.Parameters.AddWithValue(keyword);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
