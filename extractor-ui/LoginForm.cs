namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Primer paso del flujo de vinculación: inicia sesión con la cuenta humana del SaaS
/// directamente contra central-api (no contra el servicio local — ver CentralApiClient). Al
/// terminar con éxito, deja armado un <see cref="CentralApiClient"/> con la sesión activa para
/// que <see cref="BusinessBranchPickerForm"/> continúe.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly TextBox apiUrlBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox emailBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox passwordBox = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly Label errorLabel = new() { ForeColor = UiTheme.Danger, AutoSize = true, Dock = DockStyle.Top };
    private readonly Button loginButton = new() { Text = "Iniciar sesión", AutoSize = true, MinimumSize = new Size(140, 36) };

    public CentralApiClient? Client { get; private set; }

    public LoginForm(string? suggestedApiUrl)
    {
        Text = "RestaurantAgent Sync Agent — Iniciar sesión";
        Icon = AppIcon.Load();
        Width = 460;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;
        UiTheme.ApplyWindow(this, minWidth: 420, minHeight: 360);
        errorLabel.Font = UiTheme.BaseFont;

        apiUrlBox.Text = suggestedApiUrl ?? "";
        UiTheme.StyleTextBox(apiUrlBox);
        UiTheme.StyleTextBox(emailBox);
        UiTheme.StyleTextBox(passwordBox);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true,
            Padding = new Padding(24, 24, 24, 8)
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        form.Controls.Add(UiTheme.Heading("Iniciar sesión"), 0, 0);
        form.SetColumnSpan(form.Controls[^1], 2);
        form.RowCount = 1;

        AddRow(form, "URL de la plataforma:", apiUrlBox);
        AddRow(form, "Correo:", emailBox);
        AddRow(form, "Contraseña:", passwordBox);

        loginButton.Click += async (_, _) => await OnLoginAsync();
        AcceptButton = loginButton;
        UiTheme.StylePrimaryButton(loginButton);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 1,
            AutoSize = true,
            Padding = new Padding(24, 8, 24, 24)
        };
        footer.Controls.Add(errorLabel);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(loginButton);
        footer.Controls.Add(buttons);

        Controls.Add(form);
        Controls.Add(footer);
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control input)
    {
        panel.RowCount++;
        panel.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 8, 8, 0) }, 0, panel.RowCount - 1);
        input.Margin = new Padding(0, 4, 0, 0);
        panel.Controls.Add(input, 1, panel.RowCount - 1);
    }

    private async Task OnLoginAsync()
    {
        errorLabel.Text = "";
        var apiUrl = apiUrlBox.Text.Trim();
        var email = emailBox.Text.Trim();
        var password = passwordBox.Text;

        if (string.IsNullOrWhiteSpace(apiUrl) || !Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
        {
            errorLabel.Text = "La URL de la plataforma no es válida.";
            return;
        }
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            errorLabel.Text = "Correo y contraseña son obligatorios.";
            return;
        }

        loginButton.Enabled = false;
        try
        {
            var candidate = new CentralApiClient(apiUrl);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var (ok, error) = await candidate.LoginAsync(email, password, cts.Token);
            if (!ok)
            {
                errorLabel.Text = error ?? "No se pudo iniciar sesión. Verifica tu correo y contraseña.";
                candidate.Dispose();
                return;
            }

            Client = candidate;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            errorLabel.Text = "No se pudo contactar la plataforma. Revisa la URL y tu conexión a Internet.";
        }
        finally
        {
            loginButton.Enabled = true;
        }
    }
}
