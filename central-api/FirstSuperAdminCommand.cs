namespace RestaurantAgent.CentralApi;

internal static class FirstSuperAdminCommand
{
    public static bool IsRequested(string[] args) =>
        args.Any(arg => string.Equals(arg, "--create-superadmin", StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(ApiOptions options, CancellationToken ct)
    {
        Console.WriteLine("Creación del primer SUPERADMIN");
        Console.Write("Email: ");
        var email = Console.ReadLine()?.Trim() ?? string.Empty;
        Console.Write("Nombre visible (opcional): ");
        var displayName = Console.ReadLine()?.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName;
        Console.Write("Contraseña: ");
        var password = ReadSecret();
        Console.Write("Confirmar contraseña: ");
        var confirmation = ReadSecret();
        Console.WriteLine();

        if (!UserValidation.IsValidEmail(email))
            throw new InvalidOperationException("El email no tiene un formato válido.");
        if (!UserValidation.IsValidPassword(password))
            throw new InvalidOperationException($"La contraseña debe tener al menos {UserValidation.MinPasswordLength} caracteres.");
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
            throw new InvalidOperationException("Las contraseñas no coinciden.");

        await using var dataSource = Npgsql.NpgsqlDataSource.Create(options.ConnectionString);
        await DbInitializer.InitializeAsync(dataSource, options, ct);
        var auth = new WebAuthService(dataSource, options);
        var users = new UserRegistry(dataSource, auth, new SubscriptionRegistry(dataSource));
        if (!await users.CreateFirstSuperAdminAsync(email, displayName, password, ct))
            throw new InvalidOperationException("La operación fue rechazada: ya existe un SUPERADMIN.");

        Console.WriteLine("SUPERADMIN creado correctamente. La contraseña no se almacena en texto plano ni se imprime.");
    }

    private static string ReadSecret()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
        return value.ToString();
    }
}
