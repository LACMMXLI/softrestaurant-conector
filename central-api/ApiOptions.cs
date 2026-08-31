namespace RestaurantAgent.CentralApi;

internal sealed record ApiOptions(
    string ConnectionString,
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

        var sessionHours = ReadPositiveInt(configuration["DASHBOARD_SESSION_HOURS"], 24, 1, 720);
        var staleMinutes = ReadPositiveInt(configuration["DASHBOARD_STALE_MINUTES"], 10, 1, 1440);

        return new ApiOptions(
            connectionString,
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
