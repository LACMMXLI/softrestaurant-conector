namespace SoftRestaurant.CentralApi;

internal sealed record ApiOptions(
    string ConnectionString,
    string ConnectorAdminKey,
    string? BootstrapBranchCode,
    string? BootstrapBranchName,
    string? BootstrapBusinessName,
    string? DashboardOwnerEmail,
    string? DashboardOwnerPassword,
    string? DashboardAdminEmail,
    string? DashboardAdminPassword,
    int DashboardSessionHours,
    int DashboardStaleMinutes,
    string? InstallerDownloadUrl,
    string? InstallerVersion)
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

        var ownerEmail = configuration["DASHBOARD_OWNER_EMAIL"]?.Trim();
        var ownerPassword = configuration["DASHBOARD_OWNER_PASSWORD"];
        if (string.IsNullOrWhiteSpace(ownerEmail) != string.IsNullOrWhiteSpace(ownerPassword))
            throw new InvalidOperationException(
                "DASHBOARD_OWNER_EMAIL y DASHBOARD_OWNER_PASSWORD deben configurarse juntos.");
        if (!string.IsNullOrWhiteSpace(ownerPassword) && ownerPassword.Length < 12)
            throw new InvalidOperationException("DASHBOARD_OWNER_PASSWORD debe tener al menos 12 caracteres.");

        var adminEmail = configuration["DASHBOARD_ADMIN_EMAIL"]?.Trim();
        var adminPassword = configuration["DASHBOARD_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(adminEmail) != string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException(
                "DASHBOARD_ADMIN_EMAIL y DASHBOARD_ADMIN_PASSWORD deben configurarse juntos.");
        if (!string.IsNullOrWhiteSpace(adminPassword) && adminPassword.Length < 12)
            throw new InvalidOperationException("DASHBOARD_ADMIN_PASSWORD debe tener al menos 12 caracteres.");

        var sessionHours = ReadPositiveInt(configuration["DASHBOARD_SESSION_HOURS"], 24, 1, 720);
        var staleMinutes = ReadPositiveInt(configuration["DASHBOARD_STALE_MINUTES"], 10, 1, 1440);

        return new ApiOptions(
            connectionString,
            adminKey,
            configuration["BOOTSTRAP_BRANCH_CODE"],
            configuration["BOOTSTRAP_BRANCH_NAME"],
            configuration["BOOTSTRAP_BUSINESS_NAME"],
            ownerEmail,
            ownerPassword,
            adminEmail,
            adminPassword,
            sessionHours,
            staleMinutes,
            configuration["INSTALLER_DOWNLOAD_URL"],
            configuration["INSTALLER_VERSION"]);
    }

    private static int ReadPositiveInt(string? value, int fallback, int minimum, int maximum) =>
        int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum
            ? parsed
            : fallback;
}
