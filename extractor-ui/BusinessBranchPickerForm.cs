namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Segundo y último paso del flujo de vinculación: con la sesión humana ya abierta
/// (<see cref="LoginForm"/>), lista los negocios y sucursales que el usuario puede administrar,
/// muestra el estado del conector de cada sucursal, y ofrece "Vincular este equipo" o
/// "Reemplazar equipo" (si ya hay uno activo). Al confirmar, entrega la credencial resultante al
/// servicio local vía <see cref="ControlApiClient.LinkAsync"/> — nunca la guarda ella misma.
/// </summary>
public sealed class BusinessBranchPickerForm : Form
{
    private readonly CentralApiClient centralApi;
    private readonly ControlApiClient controlApi;

    private readonly ComboBox businessCombo = new() { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox branchList = new() { Width = 360, Height = 180 };
    private readonly Label statusLabel = new() { AutoSize = true, MaximumSize = new Size(360, 0) };
    private readonly Button actionButton = new() { Text = "Vincular este equipo", Width = 180 };
    private readonly Label errorLabel = new() { ForeColor = Color.DarkRed, AutoSize = true, MaximumSize = new Size(360, 0) };

    private List<BusinessMembershipDto> businesses = [];
    private List<BranchWithConnectorDto> branches = [];

    public bool Linked { get; private set; }

    public BusinessBranchPickerForm(CentralApiClient centralApi, ControlApiClient controlApi)
    {
        this.centralApi = centralApi;
        this.controlApi = controlApi;

        Text = "RestaurantAgent Sync Agent — Vincular este equipo";
        Width = 420;
        Height = 460;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, Padding = new Padding(16), AutoSize = true };
        layout.Controls.Add(new Label { Text = "Negocio:", AutoSize = true });
        layout.Controls.Add(businessCombo);
        layout.Controls.Add(new Label { Text = "Sucursal:", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        layout.Controls.Add(branchList);
        layout.Controls.Add(statusLabel);
        layout.Controls.Add(errorLabel);

        businessCombo.SelectedIndexChanged += async (_, _) => await LoadBranchesAsync();
        branchList.SelectedIndexChanged += (_, _) => RefreshSelection();
        actionButton.Click += async (_, _) => await OnActionAsync();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
        buttons.Controls.Add(actionButton);

        Controls.Add(layout);
        Controls.Add(buttons);

        Load += async (_, _) => await LoadBusinessesAsync();
    }

    private async Task LoadBusinessesAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            businesses = await centralApi.GetBusinessesAsync(cts.Token);
            businessCombo.DataSource = businesses;
            businessCombo.DisplayMember = nameof(BusinessMembershipDto.Name);
            if (businesses.Count == 0)
            {
                errorLabel.Text = "Esta cuenta todavía no tiene negocios. Créalo primero desde el panel web.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            errorLabel.Text = "No se pudo contactar la plataforma.";
        }
    }

    private async Task LoadBranchesAsync()
    {
        branchList.Items.Clear();
        statusLabel.Text = "";
        errorLabel.Text = "";
        if (businessCombo.SelectedItem is not BusinessMembershipDto business) return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            branches = await centralApi.GetBranchesAsync(business.Id, cts.Token);
            foreach (var item in branches)
            {
                var statusText = item.Connector is null
                    ? "sin conector"
                    : $"vinculado a {item.Connector.MachineName}";
                branchList.Items.Add($"{item.Branch.Name} ({item.Branch.Code}) — {statusText}");
            }
            if (branches.Count > 0) branchList.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            errorLabel.Text = "No se pudieron cargar las sucursales.";
        }
    }

    private void RefreshSelection()
    {
        errorLabel.Text = "";
        if (branchList.SelectedIndex < 0 || branchList.SelectedIndex >= branches.Count)
        {
            statusLabel.Text = "";
            actionButton.Enabled = false;
            return;
        }

        var selected = branches[branchList.SelectedIndex];
        actionButton.Enabled = true;
        if (selected.Connector is null)
        {
            statusLabel.Text = "Esta sucursal no tiene ningún equipo vinculado todavía.";
            actionButton.Text = "Vincular este equipo";
        }
        else
        {
            var connector = selected.Connector;
            statusLabel.Text =
                $"Ya vinculado a \"{connector.MachineName}\" desde {connector.LinkedAt?.ToLocalTime():dd/MM/yyyy HH:mm}.\n" +
                $"Último latido: {(connector.LastHeartbeatAt is { } h ? h.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "nunca")}.\n" +
                "Vincular este equipo reemplazará al anterior — dejará de sincronizar de inmediato.";
            actionButton.Text = "Reemplazar equipo";
        }
    }

    private async Task OnActionAsync()
    {
        if (branchList.SelectedIndex < 0 || branchList.SelectedIndex >= branches.Count) return;
        var selected = branches[branchList.SelectedIndex];
        errorLabel.Text = "";

        if (selected.Connector is not null)
        {
            var confirm = MessageBox.Show(this,
                $"\"{selected.Connector.MachineName}\" dejará de sincronizar esta sucursal de inmediato. ¿Continuar?",
                "Reemplazar equipo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        actionButton.Enabled = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            DeviceCredentialDto credential;
            if (selected.Connector is null)
            {
                var outcome = await centralApi.LinkDeviceAsync(selected.Branch.Code, Environment.MachineName, cts.Token);
                if (!outcome.Succeeded)
                {
                    // Carrera: otra persona vinculó esta sucursal entre que se listó y se
                    // confirmó. Recargar y dejar que el usuario decida con el estado real.
                    errorLabel.Text = "Esta sucursal ya tiene un conector activo. Actualizando lista…";
                    await LoadBranchesAsync();
                    return;
                }
                credential = outcome.Credential!;
            }
            else
            {
                credential = await centralApi.ReplaceDeviceAsync(selected.Branch.Code, Environment.MachineName, cts.Token);
            }

            var handoff = new LinkDeviceCredentialDto
            {
                InstallationId = credential.InstallationId.ToString(),
                BranchCode = credential.BranchCode,
                BusinessId = credential.BusinessId.ToString(),
                Token = credential.Token,
                ApiUrl = credential.ApiUrl
            };
            using var linkCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (!await controlApi.LinkAsync(handoff, linkCts.Token))
            {
                errorLabel.Text = "central-api emitió la credencial, pero el servicio local no la aceptó. ¿Está corriendo el servicio de Windows?";
                return;
            }

            Linked = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            errorLabel.Text = "No se pudo completar la vinculación. Intenta de nuevo.";
        }
        finally
        {
            actionButton.Enabled = true;
        }
    }
}
