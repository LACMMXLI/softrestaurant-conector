namespace SoftRestaurant.Extractor.Ui;

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
        menu.Items.Add("Vincular / reemplazar equipo…", null, async (_, _) => await RunLinkFlowAsync());
        menu.Items.Add("Sincronizar ahora", null, async (_, _) => await SyncNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApplication());

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SoftRestaurant Sync Agent",
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

            trayIcon.ShowBalloonTip(6000, "SoftRestaurant Sync Agent",
                "Este equipo todavía no está vinculado a ninguna sucursal. Haz clic aquí para vincularlo.",
                ToolTipIcon.Info);
            await RunLinkFlowAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // El servicio puede tardar unos segundos más en levantar la API de control tras un
            // arranque de Windows; no interrumpir con un diálogo, el usuario puede pedirlo desde
            // el menú "Vincular / reemplazar equipo…" en cualquier momento.
        }
    }

    private async Task RunLinkFlowAsync()
    {
        string? suggestedApiUrl = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            suggestedApiUrl = (await client.GetConfigAsync(cts.Token))?.ApiUrl;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Sin config disponible, el usuario escribe la URL a mano en LoginForm.
        }

        using var login = new LoginForm(suggestedApiUrl);
        if (login.ShowDialog() != DialogResult.OK || login.Client is null) return;

        using var picker = new BusinessBranchPickerForm(login.Client, client);
        if (picker.ShowDialog() == DialogResult.OK && picker.Linked)
        {
            trayIcon.ShowBalloonTip(4000, "SoftRestaurant Sync Agent",
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
