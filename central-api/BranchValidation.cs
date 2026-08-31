using System.Text.RegularExpressions;

namespace RestaurantAgent.CentralApi;

/// <summary>
/// Reglas de validación de sucursal compartidas entre las rutas /api/web/* (lectura) y
/// /api/admin/* (alta/edición). Vive en un solo lugar para que ambas familias de endpoints
/// no diverjan en qué código o zona horaria consideran válidos.
/// </summary>
internal static partial class BranchValidation
{
    public const int MaxNameLength = 200;
    public const int MaxTimezoneLength = 100;

    public static bool IsValidCode(string? code) =>
        !string.IsNullOrEmpty(code) && CodePattern().IsMatch(code);

    public static bool IsValidTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Length > MaxTimezoneLength) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant)]
    public static partial Regex CodePattern();
}
