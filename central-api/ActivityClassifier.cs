namespace RestaurantAgent.CentralApi;

/// <summary>Qué hizo el usuario, ya traducido a claves estables y sin datos sensibles.</summary>
internal sealed record ActivityDescriptor(
    string EventType,
    string Action,
    string? View,
    string? BranchCode,
    Guid? BusinessId,
    IReadOnlyDictionary<string, object?> Metadata);

/// <summary>Cuerpo de <c>POST /api/web/activity</c>: lo que la app reporta de su propia navegación.</summary>
internal sealed record ClientActivityRequest(string? Kind, string? View, string? BranchCode, Guid? BusinessId, string? Detail);

/// <summary>
/// Convierte una petición a /api/web/* en un evento de actividad legible. Es una función pura
/// (sin HttpContext ni base de datos) para poder probar la privacidad del registro:
/// <list type="bullet">
/// <item>solo se conservan parámetros de una lista blanca (fechas, turno, página, tipo, folio);</item>
/// <item>los textos de búsqueda libres (<c>search</c>, <c>user</c>, <c>product</c>) nunca se guardan,
/// solo el hecho de que se filtró;</item>
/// <item>jamás se lee el cuerpo de la petición, así que contraseñas y datos de formularios no llegan aquí.</item>
/// </list>
/// </summary>
internal static class ActivityClassifier
{
    public const string WebPrefix = "/api/web";

    public static readonly IReadOnlySet<string> ClientViews = new HashSet<string>(StringComparer.Ordinal)
    {
        "home", "sales", "operations", "cancellations", "products", "businesses", "more", "expense-categories",
    };

    private static readonly string[] MetadataQueryKeys = ["date", "from", "to", "shiftId", "page", "type"];
    private static readonly string[] FreeTextQueryKeys = ["search", "user", "product"];
    private const int MaxMetadataValueLength = 40;
    private const int MaxDetailLength = 120;

    /// <returns><c>null</c> cuando la petición no debe registrarse (ruido o la propia ruta de registro).</returns>
    public static ActivityDescriptor? Describe(string method, string path, IReadOnlyDictionary<string, string> query)
    {
        if (!path.StartsWith(WebPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        var segments = path[WebPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        var verb = method.ToUpperInvariant();
        var branch = Clean(query.GetValueOrDefault("branchCode"), 100);
        Guid? business = Guid.TryParse(query.GetValueOrDefault("businessId"), out var queryBusiness) ? queryBusiness : null;
        var metadata = new Dictionary<string, object?>();
        foreach (var key in MetadataQueryKeys)
        {
            var value = Clean(query.GetValueOrDefault(key), MaxMetadataValueLength);
            if (value is not null) metadata[key] = value;
        }
        if (FreeTextQueryKeys.Any(key => !string.IsNullOrWhiteSpace(query.GetValueOrDefault(key))))
            metadata["filtered"] = true;

        ActivityDescriptor Make(string eventType, string action, string? view) =>
            new(eventType, action, view, branch, business, metadata);

        var first = segments[0].ToLowerInvariant();
        var second = segments.Length > 1 ? segments[1].ToLowerInvariant() : null;

        switch (first)
        {
            case "auth":
                return second switch
                {
                    "login" when verb == "POST" => Make("auth", "auth.login", null),
                    "register" when verb == "POST" => Make("auth", "auth.register", null),
                    "logout" when verb == "POST" => Make("auth", "auth.logout", null),
                    // Cada carga o recarga de la app consulta la sesión: es lo que cuenta como "visita".
                    "me" when verb == "GET" => Make("session", "app_open", null),
                    _ => null,
                };

            case "dashboard":
                return second switch
                {
                    "home" => Make("view", "dashboard.home", "home"),
                    "business-home" => Make("view", "dashboard.business_home", "home"),
                    _ => null, // /dashboard/shifts es carga de contexto, no una interacción
                };

            case "sales":
                if (segments.Length == 1) return Make("view", "sales.list", "sales");
                if (segments.Length == 3)
                {
                    branch = Clean(segments[1], 100);
                    if (long.TryParse(segments[2], out var folio)) metadata["folio"] = folio;
                    return Make("view", "ticket.open", "sales");
                }
                return null;

            case "open-accounts":
                if (segments.Length != 3) return null;
                branch = Clean(segments[1], 100);
                if (long.TryParse(segments[2], out var tempFolio)) metadata["folio"] = tempFolio;
                return Make("view", "open_account.open", "home");

            case "cash-movements":
                return Make("view", "operations.cash_movements", "operations");

            case "expenses":
                return DescribeExpenses(verb, second, Make);

            case "product-cancellations":
                return Make("view", "cancellations.list", "cancellations");

            case "businesses":
                if (segments.Length == 1)
                    return verb == "POST" ? Make("action", "business.create", "businesses") : Make("view", "businesses.list", "businesses");
                if (Guid.TryParse(segments[1], out var routeBusiness))
                {
                    business = routeBusiness;
                    if (segments.Length == 3 && segments[2].Equals("branches", StringComparison.OrdinalIgnoreCase))
                        return verb == "POST" ? Make("action", "branch.create", "businesses") : Make("view", "business.branches", "businesses");
                }
                return null;

            case "branches":
                if (segments.Length < 3) return null; // GET /branches es carga de contexto
                branch = Clean(segments[1], 100);
                return segments[2].ToLowerInvariant() switch
                {
                    "connector" or "connector-installations" when verb == "GET" => Make("view", "connector.view", "businesses"),
                    "link-device" when verb == "POST" => Make("action", "connector.link", "businesses"),
                    "replace-device" when verb == "POST" => Make("action", "connector.replace", "businesses"),
                    "request-sync" when verb == "POST" => Make("action", "sync.request", "more"),
                    _ => null,
                };

            case "connector-installations":
                return segments.Length == 3 && verb == "POST" && segments[2].Equals("revoke", StringComparison.OrdinalIgnoreCase)
                    ? Make("action", "connector.revoke", "businesses")
                    : null;

            case "agent":
                return second == "latest" ? Make("view", "installer.latest", "businesses") : null;

            case "activity":
                return null; // la propia ruta de registro no se registra a sí misma

            default:
                return Make("api", "api.other", null);
        }
    }

    private static ActivityDescriptor? DescribeExpenses(
        string verb, string? second, Func<string, string, string?, ActivityDescriptor> make) => second switch
        {
            "summary" => make("view", "operations.expense_summary", "operations"),
            "categories" => verb switch
            {
                "GET" => make("view", "expense_categories.list", "expense-categories"),
                "POST" => make("action", "expense_category.create", "expense-categories"),
                "PUT" => make("action", "expense_category.update", "expense-categories"),
                "DELETE" => make("action", "expense_category.delete", "expense-categories"),
                _ => null,
            },
            "movements" => verb switch
            {
                "PUT" => make("action", "expense.assign_category", "operations"),
                "DELETE" => make("action", "expense.reset_category", "operations"),
                _ => null,
            },
            _ => null,
        };

    /// <summary>Un intento de login que falló no tiene usuario: se anota como tal, sin guardar el correo tecleado.</summary>
    public static ActivityDescriptor Refine(ActivityDescriptor descriptor, int status) =>
        descriptor.Action is "auth.login" or "auth.register" && status >= 400
            ? descriptor with { Action = descriptor.Action + "_failed" }
            : descriptor;

    /// <summary>Valida un evento reportado por la app; <c>null</c> si algún campo no pertenece a la lista permitida.</summary>
    public static ActivityDescriptor? DescribeClient(ClientActivityRequest request)
    {
        var (eventType, action) = request.Kind switch
        {
            "view" => ("view", "ui.tab"),
            "scope" => ("context", "ui.scope"),
            "filter" => ("context", "ui.filter"),
            "refresh" => ("action", "ui.refresh"),
            _ => (null, null),
        };
        if (eventType is null || action is null) return null;
        if (request.View is not null && !ClientViews.Contains(request.View)) return null;

        var metadata = new Dictionary<string, object?>();
        var detail = Clean(request.Detail, MaxDetailLength);
        if (detail is not null) metadata["detail"] = detail;
        return new ActivityDescriptor(eventType, action, request.View, Clean(request.BranchCode, 100), request.BusinessId, metadata);
    }

    /// <summary>Recorta y elimina caracteres de control; devuelve <c>null</c> si queda vacío.</summary>
    public static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (cleaned.Length == 0) return null;
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }
}
