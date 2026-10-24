using System.Diagnostics;

namespace RestaurantAgent.CentralApi;

/// <summary>
/// Middleware que registra cada interacción de una cuenta con /api/web/*. Quién (usuario ya
/// autenticado), dónde (sucursal/negocio de la ruta o del query) y qué (clave de acción) se
/// resuelven aquí; el guardado lo hace <see cref="ActivityLogger"/> en segundo plano.
/// Las visitas anónimas (sin sesión) no se guardan —salvo intentos de login/registro, que ya
/// están limitados por IP— para que tráfico no autenticado no pueda inflar la base de datos.
/// </summary>
internal static class ActivityTracking
{
    public static void UseActivityTracking(this WebApplication app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(ActivityClassifier.WebPrefix))
            {
                await next();
                return;
            }

            var descriptor = ActivityClassifier.Describe(
                context.Request.Method, context.Request.Path.Value ?? string.Empty, ReadQuery(context.Request));
            if (descriptor is null)
            {
                await next();
                return;
            }

            var auth = context.RequestServices.GetRequiredService<WebAuthService>();
            try { await auth.AuthenticateAsync(context, context.RequestAborted); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                app.Logger.LogWarning(ex, "No se pudo identificar al usuario para el registro de actividad.");
            }

            var timer = Stopwatch.StartNew();
            var status = StatusCodes.Status500InternalServerError; // si next() lanza, el manejador global responde 500
            try
            {
                await next();
                status = context.Response.StatusCode;
            }
            finally
            {
                timer.Stop();
                var user = WebAuthService.GetAuthenticatedUser(context);
                if (user is not null || descriptor.EventType == "auth")
                {
                    try
                    {
                        context.RequestServices.GetRequiredService<ActivityLogger>().Record(
                            BuildEntry(context, ActivityClassifier.Refine(descriptor, status), user, "api", status, (int)timer.ElapsedMilliseconds));
                    }
                    catch (Exception ex)
                    {
                        app.Logger.LogWarning(ex, "No se pudo encolar el evento de actividad.");
                    }
                }
            }
        });

    internal static ActivityEntry BuildEntry(
        HttpContext context, ActivityDescriptor descriptor, DashboardUser? user, string source, int? status, int? durationMs)
    {
        // Referencia corta del hash de sesión: sirve para agrupar una visita, no para reconstruir la cookie.
        string? sessionRef = null;
        if (context.Request.Cookies.TryGetValue(WebAuthService.CookieName, out var token) && !string.IsNullOrWhiteSpace(token))
            sessionRef = TokenHasher.Hash(token)[..12];

        var isApi = source == "api";
        return new ActivityEntry(
            DateTime.UtcNow,
            user?.Id,
            user?.Email,
            user?.DisplayName,
            source,
            descriptor.EventType,
            descriptor.Action,
            descriptor.View,
            isApi ? context.Request.Method : null,
            isApi ? ActivityClassifier.Clean(context.Request.Path.Value, 300) : null,
            status,
            durationMs,
            descriptor.BranchCode,
            descriptor.BusinessId,
            ActivityClassifier.Clean(context.Connection.RemoteIpAddress?.ToString(), 100),
            ActivityClassifier.Clean(context.Request.Headers.UserAgent.ToString(), 300),
            sessionRef,
            ActivityLogger.SerializeMetadata(descriptor.Metadata));
    }

    private static Dictionary<string, string> ReadQuery(HttpRequest request) =>
        request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
}
