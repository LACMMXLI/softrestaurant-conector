using System.Text.RegularExpressions;

namespace SoftRestaurant.CentralApi;

/// <summary>Reglas de validación para las cuentas administradas desde /api/admin/users.</summary>
internal static partial class UserValidation
{
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 200;
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 1024;

    public static readonly IReadOnlyCollection<string> ValidRoles =
        ["SUPERADMIN", "OWNER", "MANAGER", "VIEWER"];

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Length <= MaxEmailLength &&
        EmailPattern().IsMatch(email);

    /// <summary>Misma política que DASHBOARD_OWNER_PASSWORD / DASHBOARD_ADMIN_PASSWORD: 12+ caracteres.</summary>
    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= MinPasswordLength &&
        password.Length <= MaxPasswordLength;

    public static bool IsValidRole(string? role) =>
        role is not null && ValidRoles.Contains(role);

    public static bool IsValidDisplayName(string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && displayName.Length <= MaxDisplayNameLength;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
