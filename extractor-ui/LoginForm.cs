namespace SoftRestaurant.Extractor.Ui;

/// <summary>
/// Primer paso del flujo de vinculación: inicia sesión con la cuenta humana del SaaS
/// directamente contra central-api (no contra el servicio local — ver CentralApiClient). Al
/// terminar con éxito, deja armado un <see cref="CentralApiClient"/> con la sesión activa para
/// que <see cref="BusinessBranchPickerForm"/> continúe.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly TextBox apiUrlBox = new() { Width = 320 };
    private readonly TextBox emailBox = new() { Width = 320 };
    private readonly TextBox passwordBox = new() { Width = 320, UseSystemPasswordChar = true };
    private readonly Label errorLabel = new() { ForeColor = Color.DarkRed, AutoSize = true, MaximumSize = new Size(320, 0) };
    private readonly Button loginButton = new() { Text = "Iniciar sesión", Width = 120 };

    public CentralApiClient? Client { get; private set; }

    public LoginForm(string? suggestedApiUrl)
    {
        Text = "SoftRestaurant Sync Agent — Iniciar sesión";
        Width = 400;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        apiUrlBox.Text = suggestedApiUrl ?? "";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), AutoSize = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, "URL de la plataforma:", apiUrlBox);
        AddRow(layout, "Correo:", emailBox);
        AddRow(layout, "Contraseña:", passwordBox);
        layout.RowCount++;
        layout.Controls.Add(new Label(), 0, layout.RowCount - 1);
        layout.Controls.Add(errorLabel, 1, layout.RowCount - 1);

        loginButton.Click += async (_, _) => await OnLoginAsync();
        AcceptButton = loginButton;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16) };
        buttons.Controls.Add(loginButton);

        Controls.Add(layout);
        Controls.Add(buttons);
    }

    private static void AddRow(TableLayoutPanel panel, string caption, Control input)
    {
        panel.RowCount++;
        panel.Controls.Add(new Label { Text = caption, AutoSize = true }, 0, panel.RowCount - 1);
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
