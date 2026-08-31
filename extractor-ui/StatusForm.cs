using System.ComponentModel;

namespace SoftRestaurant.Extractor.Ui;

/// <summary>
/// Ventana de estado del agente. Consulta la API de control local cada 5s; nunca toca la
/// configuración protegida ni el servicio de Windows directamente — todo pasa por HTTP local.
/// Cerrar esta ventana (o toda la GUI) NO detiene el servicio: sigue corriendo aparte.
/// </summary>
public sealed class StatusForm : Form
{
    private readonly ControlApiClient client;
    private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 5000 };

    private readonly Label serviceLabel = new();
    private readonly Label sqlLabel = new();
    private readonly Label apiLabel = new();
    private readonly Label lastSyncLabel = new();
    private readonly Label pendingLabel = new();
    private readonly Label errorLabel = new();
    private readonly Button syncNowButton = new() { Text = "Sincronizar ahora" };
    private readonly Button diagnosticsButton = new() { Text = "Diagnóstico" };
    private readonly Button logsButton = new() { Text = "Ver logs" };
    private readonly TextBox logsBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Visible = false };

    public StatusForm(int controlPort)
    {
        client = new ControlApiClient(controlPort);

        Text = "SoftRestaurant Sync Agent — Estado";
        Width = 480;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        FormClosing += OnFormClosing;

        BuildLayout();

        pollTimer.Tick += async (_, _) => await RefreshStatusAsync();
        pollTimer.Start();
        _ = RefreshStatusAsync();
    }

    private void BuildLayout()
    {
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(16)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(stack, "Servicio:", serviceLabel);
        AddRow(stack, "Conexión SoftRestaurant (SQL):", sqlLabel);
        AddRow(stack, "Conexión con el servidor:", apiLabel);
        AddRow(stack, "Última sincronización correcta:", lastSyncLabel);
        AddRow(stack, "Datos pendientes de enviar:", pendingLabel);
        AddRow(stack, "Último error:", errorLabel);
        errorLabel.MaximumSize = new Size(300, 0);
        errorLabel.AutoSize = true;

        var buttonsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 0, 16, 8) };
        syncNowButton.Click += async (_, _) => await OnSyncNowAsync();
        diagnosticsButton.Click += async (_, _) => await OnDiagnosticsAsync();
        logsButton.Click += async (_, _) => await OnToggleLogsAsync();
        buttonsPanel.Controls.Add(syncNowButton);
        buttonsPanel.Controls.Add(diagnosticsButton);
        buttonsPanel.Controls.Add(logsButton);

        logsBox.Dock = DockStyle.Fill;
        logsBox.Font = new Font(FontFamily.GenericMonospace, 8.5f);

        Controls.Add(logsBox);
        Controls.Add(buttonsPanel);
        Controls.Add(stack);
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Label valueLabel)
    {
        panel.RowCount++;
        panel.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) });
        valueLabel.Text = "—";
        valueLabel.AutoSize = true;
        panel.Controls.Add(valueLabel);
    }

    private async Task RefreshStatusAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var status = await client.GetStatusAsync(cts.Token);
            if (status is null) { ShowServiceUnavailable(); return; }

            serviceLabel.Text = $"En ejecución ({status.State})";
            serviceLabel.ForeColor = status.State == "Error" ? Color.DarkRed : Color.DarkGreen;

            sqlLabel.Text = FormatConnectivity(status.SqlConnected);
            apiLabel.Text = status.SendEnabled ? FormatConnectivity(status.ApiConnected) : "No configurado (modo local)";
            lastSyncLabel.Text = status.LastSuccessAt is { } lastSuccess
                ? lastSuccess.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")
                : "Todavía no hay una sincronización correcta";
            pendingLabel.Text = status.PendingBatches.ToString();
            errorLabel.Text = string.IsNullOrWhiteSpace(status.LastError) ? "Ninguno" : status.LastError;
            errorLabel.ForeColor = string.IsNullOrWhiteSpace(status.LastError) ? SystemColors.ControlText : Color.DarkRed;

            syncNowButton.Enabled = status.State != "Syncing";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            ShowServiceUnavailable();
        }
    }

    private void ShowServiceUnavailable()
    {
        serviceLabel.Text = "No disponible (¿el servicio está detenido?)";
        serviceLabel.ForeColor = Color.DarkRed;
        sqlLabel.Text = "—";
        apiLabel.Text = "—";
        syncNowButton.Enabled = false;
    }

    private async Task OnSyncNowAsync()
    {
        syncNowButton.Enabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await client.RequestSyncNowAsync(cts.Token);
            if (!result.Started)
                MessageBox.Show(this, result.Error ?? "No se pudo iniciar la sincronización.", "Sincronizar ahora",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            MessageBox.Show(this, "El servicio no está disponible en este momento.", "Sincronizar ahora",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            await RefreshStatusAsync();
        }
    }

    private async Task OnDiagnosticsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var report = await client.GetDiagnosticsAsync(cts.Token);
            if (report is null) { MessageBox.Show(this, "Sin respuesta del agente.", "Diagnóstico"); return; }

            var lines = report.Checks.Select(c => $"[{(c.Ok ? "OK" : "FALLA")}] {c.Name}: {c.Detail}");
            MessageBox.Show(this, string.Join(Environment.NewLine, lines),
                report.Ok ? "Diagnóstico — todo correcto" : "Diagnóstico — hay problemas",
                MessageBoxButtons.OK, report.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            MessageBox.Show(this, "El servicio no está disponible en este momento.", "Diagnóstico",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task OnToggleLogsAsync()
    {
        logsBox.Visible = !logsBox.Visible;
        if (!logsBox.Visible) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var lines = await client.GetLogsAsync(200, cts.Token);
            logsBox.Text = lines is null or { Count: 0 }
                ? "Sin actividad registrada todavía."
                : string.Join(Environment.NewLine, lines);
            logsBox.SelectionStart = logsBox.Text.Length;
            logsBox.ScrollToCaret();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logsBox.Text = "No se pudo obtener el registro (¿el servicio está detenido?).";
        }
    }

    private static string FormatConnectivity(bool? connected) =>
        connected switch { true => "Conectado", false => "Sin conexión", null => "Desconocido" };

    private void OnFormClosing(object? sender, CancelEventArgs e)
    {
        // Cerrar esta ventana no cierra la app de bandeja ni el servicio: solo la oculta.
        e.Cancel = true;
        Hide();
    }
}
