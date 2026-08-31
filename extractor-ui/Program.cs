namespace SoftRestaurant.Extractor.Ui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, "SoftRestaurantSyncAgent.Ui.SingleInstance", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("El panel del agente ya está abierto (revisa la bandeja del sistema).",
                "SoftRestaurant Sync Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();

        var controlPort = 47811;
        if (int.TryParse(Environment.GetEnvironmentVariable("SRX_CONTROL_PORT"), out var envPort) &&
            envPort is > 0 and <= 65535)
        {
            controlPort = envPort;
        }

        Application.Run(new TrayApplicationContext(controlPort));
    }
}
