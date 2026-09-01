namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Contexto de la app de bandeja: vive mientras haya un ícono en la bandeja del sistema, sin
/// una ventana principal visible por defecto. "Salir" del menú solo cierra esta app — el
/// servicio de Windows sigue sincronizando igual, esté o no la GUI abierta.
///
/// Si el equipo todavía no está vinculado a ninguna sucursal (instalación recién hecha, sin
/// código de activación que pedir en el instalador), al arrancar se ofrece el flujo de login +
/// selección de negocio/sucursal en vez de la vista de estado normal.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon trayIcon;
    private readonly StatusForm statusForm;
    private readonly ControlApiClient client;

    public TrayApplicationContext(int controlPort)
    {
        client = new ControlApiClient(controlPort);
        statusForm = new StatusForm(controlPort);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir panel", null, (_, _) => ShowStatusForm());
        menu.Items.Add("Vincular / cerrar sesión del equipo…", null, async (_, _) => await RunLinkOrUnlinkFlowAsync());
        menu.Items.Add("Sincronizar ahora", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApplication());

        trayIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(),
            Text = "RestaurantAgent Sync Agent",
            Visible = true,
            ContextMenuStrip = menu
        };
        trayIcon.DoubleClick += (_, _) => ShowStatusForm();

        _ = EnsureLinkedOnStartupAsync();
    }

    private async Task EnsureLinkedOnStartupAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var config = await client.GetConfigAsync(cts.Token);
            if (config is null || config.Linked) return;

            trayIcon.ShowBalloonTip(6000, "RestaurantAgent Sync Agent",
                "Este equipo todavía no está vinculado a ninguna sucursal. Haz clic aquí para vincularlo.",
                ToolTipIcon.Info);
            await RunLinkFlowAsync(config.ApiUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // El servicio puede tardar unos segundos más en levantar la API de control tras un
            // arranque de Windows; no interrumpir con un diálogo, el usuario puede pedirlo desde
            // el menú "Vincular / cerrar sesión del equipo…" en cualquier momento.
        }
    }

    /// <summary>
    /// Punto de entrada único para el ítem de menú "Vincular / cerrar sesión del equipo…":
    /// consulta el estado real del servicio y decide si corresponde vincular (equipo libre) o
    /// pedir confirmación para cerrar sesión (equipo ya vinculado). Antes de este chequeo, el
    /// menú relanzaba login+selección de sucursal sin importar si ya había un vínculo activo.
    /// </summary>
    private async Task RunLinkOrUnlinkFlowAsync()
    {
        AgentControlConfigDto? config;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            config = await client.GetConfigAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            MessageBox.Show("El servicio no está disponible en este momento.", "Vincular equipo",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (config is { Linked: true })
        {
            await RunUnlinkFlowAsync(config);
            return;
        }

        await RunLinkFlowAsync(config?.ApiUrl);
    }

    /// <summary>Pide confirmación y cierra la sesión del equipo (borra la identidad de dispositivo local). Nunca se llama sin confirmar primero: es la única puerta para volver a vincular.</summary>
    private async Task RunUnlinkFlowAsync(AgentControlConfigDto config)
    {
        var confirm = MessageBox.Show(
            $"Este equipo ya está vinculado a la sucursal \"{config.BranchCode}\". " +
            "Para vincularlo a otra sucursal (o a la misma con otra cuenta) primero debes cerrar " +
            "esta sesión.\n\n¿Cerrar sesión y desvincular este equipo ahora?",
            "Cerrar sesión del equipo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ok = await client.UnlinkAsync(cts.Token);
            if (!ok)
            {
                MessageBox.Show("No se pudo cerrar la sesión del equipo.", "Cerrar sesión del equipo",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            trayIcon.ShowBalloonTip(4000, "RestaurantAgent Sync Agent",
                "Sesión cerrada. Este equipo ya no está vinculado a ninguna sucursal.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            MessageBox.Show("El servicio no está disponible en este momento.", "Cerrar sesión del equipo",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunLinkFlowAsync(string? suggestedApiUrl)
    {
        if (suggestedApiUrl is null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                suggestedApiUrl = (await client.GetConfigAsync(cts.Token))?.ApiUrl;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Sin config disponible, el usuario escribe la URL a mano en LoginForm.
            }
        }

        using var login = new LoginForm(suggestedApiUrl);
        if (login.ShowDialog() != DialogResult.OK || login.Client is null) return;

        using var picker = new BusinessBranchPickerForm(login.Client, client);
        if (picker.ShowDialog() == DialogResult.OK && picker.Linked)
        {
            trayIcon.ShowBalloonTip(4000, "RestaurantAgent Sync Agent",
                "Equipo vinculado correctamente. La sincronización comenzará en breve.", ToolTipIcon.Info);
        }
    }

    private void ShowStatusForm()
    {
        statusForm.Show();
        statusForm.WindowState = FormWindowState.Normal;
        statusForm.Activate();
    }

    private async Task SyncNowAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await client.RequestSyncNowAsync(cts.Token);
            if (!result.Started)
            {
                trayIcon.ShowBalloonTip(4000, "Sincronizar ahora",
                    result.Error ?? "No se pudo iniciar la sincronización.", ToolTipIcon.Warning);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            trayIcon.ShowBalloonTip(4000, "Sincronizar ahora",
                "El servicio no está disponible en este momento.", ToolTipIcon.Error);
        }
    }

    private void ExitApplication()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        statusForm.Dispose();
        Application.ExitThread();
    }
}
