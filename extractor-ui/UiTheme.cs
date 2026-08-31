namespace RestaurantAgent.Extractor.Ui;

/// <summary>
/// Paleta y helpers de estilo compartidos por las ventanas de la GUI de bandeja. Todo con
/// WinForms puro (sin dependencias nuevas) — solo tipografía, color, tamaños mínimos y
/// comportamiento de ventana consistentes entre <see cref="LoginForm"/>,
/// <see cref="BusinessBranchPickerForm"/> y <see cref="StatusForm"/>.
/// </summary>
internal static class UiTheme
{
    public static readonly Color Background = Color.White;
    public static readonly Color Surface = Color.FromArgb(246, 247, 249);
    public static readonly Color Primary = Color.FromArgb(37, 99, 235);
    public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);
    public static readonly Color PrimaryPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
    public static readonly Color TextSecondary = Color.FromArgb(107, 114, 128);
    public static readonly Color Border = Color.FromArgb(209, 213, 219);
    public static readonly Color Danger = Color.FromArgb(185, 28, 28);

    public static readonly Font BaseFont = new("Segoe UI", 9.5f);
    public static readonly Font BoldFont = new("Segoe UI", 9.5f, FontStyle.Bold);
    public static readonly Font HeadingFont = new("Segoe UI", 13f, FontStyle.Bold);

    /// <summary>
    /// Deja la ventana redimensionable y maximizable con un tamaño mínimo razonable, para que
    /// nunca se pueda encoger hasta ocultar contenido (botones incluidos).
    /// </summary>
    public static void ApplyWindow(Form form, int minWidth, int minHeight)
    {
        form.Font = BaseFont;
        form.BackColor = Background;
        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.MaximizeBox = true;
        form.MinimizeBox = true;
        form.MinimumSize = new Size(minWidth, minHeight);
    }

    public static void StylePrimaryButton(Button button)
    {
        button.Font = BoldFont;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = PrimaryHover;
        button.FlatAppearance.MouseDownBackColor = PrimaryPressed;
        button.BackColor = Primary;
        button.ForeColor = Color.White;
        button.Height = 36;
        button.MinimumSize = new Size(140, 36);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleSecondaryButton(Button button)
    {
        button.Font = BaseFont;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Surface;
        button.BackColor = Color.White;
        button.ForeColor = TextPrimary;
        button.Height = 36;
        button.MinimumSize = new Size(110, 36);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleTextBox(TextBoxBase box)
    {
        box.Font = BaseFont;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleComboBox(ComboBox box) => box.Font = BaseFont;

    public static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = HeadingFont,
        ForeColor = TextPrimary,
        Margin = new Padding(0, 0, 0, 12)
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = TextSecondary,
        Font = BaseFont
    };
}
