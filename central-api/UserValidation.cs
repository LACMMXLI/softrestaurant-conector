using System.Text.RegularExpressions;

namespace RestaurantAgent.CentralApi;

/// <summary>Reglas de validación para las cuentas administradas desde /api/admin/users y el autorregistro en /api/web/auth/register.</summary>
internal static partial class UserValidation
{
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 200;
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 1024;

    /// <summary>Rol de cuenta (app_users.role): solo distingue operador de plataforma de cuenta normal.</summary>
    public static readonly IReadOnlyCollection<string> ValidAccountRoles = ["SUPERADMIN", "USER"];

    /// <summary>Rol de membresía de negocio (business_members.role).</summary>
    public static readonly IReadOnlyCollection<string> ValidBusinessRoles = ["OWNER", "MANAGER", "VIEWER"];

    public static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Length <= MaxEmailLength &&
        EmailPattern().IsMatch(email);

    /// <summary>Política de contraseñas de las cuentas gestionadas desde la aplicación.</summary>
    public static bool IsValidPassword(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length >= MinPasswordLength &&
        password.Length <= MaxPasswordLength;

    public static bool IsValidAccountRole(string? role) =>
        role is not null && ValidAccountRoles.Contains(role);

    public static bool IsValidBusinessRole(string? role) =>
        role is not null && ValidBusinessRoles.Contains(role);

    public static bool IsValidDisplayName(string? displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && displayName.Length <= MaxDisplayNameLength;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
