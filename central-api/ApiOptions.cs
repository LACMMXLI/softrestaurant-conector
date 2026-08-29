namespace SoftRestaurant.CentralApi;

internal sealed record ApiOptions(
    string ConnectionString,
    string ConnectorAdminKey,
    string? BootstrapBranchCode,
    string? BootstrapBranchName,
    string? LegacyBootstrapAgentToken)
{
    public static ApiOptions FromConfiguration(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? configuration["DATABASE_CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings__Database o DATABASE_CONNECTION_STRING.");

        var adminKey = configuration["CONNECTOR_ADMIN_KEY"] ?? string.Empty;
        if (adminKey.Length is > 0 and < 32)
            throw new InvalidOperationException("CONNECTOR_ADMIN_KEY debe tener al menos 32 caracteres.");

        return new ApiOptions(
            connectionString,
            adminKey,
            configuration["BOOTSTRAP_BRANCH_CODE"],
            configuration["BOOTSTRAP_BRANCH_NAME"],
            configuration["LEGACY_BOOTSTRAP_AGENT_TOKEN"] ?? configuration["BOOTSTRAP_AGENT_TOKEN"]);
    }
}
