namespace TabCloser.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using SingleInstance instance = new("Local\\TabCloser.Windows");
        if (!instance.IsPrimary)
        {
            return;
        }

        try
        {
            using TrayApplicationContext context = new();
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"TabCloser could not start.\n\n{exception.Message}",
                "TabCloser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
