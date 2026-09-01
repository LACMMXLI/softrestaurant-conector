namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Ícono de marca de la app (ver Assets/AppIcon.ico, referenciado como &lt;ApplicationIcon&gt; en el
/// .csproj y por lo tanto ya embebido en el .exe publicado). Se extrae en tiempo de ejecución con
/// <see cref="Icon.ExtractAssociatedIcon"/> en vez de duplicarlo como recurso aparte, así el
/// ícono de la bandeja y el de la barra de tareas/Explorador nunca pueden desincronizarse.
/// </summary>
internal static class AppIcon
{
    private static Icon? cached;

    public static Icon Load()
    {
        if (cached is not null) return cached;
        try
        {
            cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // No debería pasar (el ícono viene del propio ejecutable), pero si algo raro ocurre
            // con el publish (p. ej. un host distinto a Application.ExecutablePath) es mejor
            // caer al genérico de Windows que tumbar la app de bandeja.
            cached = null;
        }
        return cached ?? SystemIcons.Application;
    }
}
