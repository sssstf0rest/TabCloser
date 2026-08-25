namespace DoubleClickCloseTab.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using SingleInstance instance = new("Local\\DoubleClickCloseTab.Windows");
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
                $"Double-Click Close Tab could not start.\n\n{exception.Message}",
                "Double-Click Close Tab",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
