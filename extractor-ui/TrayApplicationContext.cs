namespace SoftRestaurant.Extractor.Ui;

/// <summary>
/// Contexto de la app de bandeja: vive mientras haya un ícono en la bandeja del sistema, sin
/// una ventana principal visible por defecto. "Salir" del menú solo cierra esta app — el
/// servicio de Windows sigue sincronizando igual, esté o no la GUI abierta.
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
