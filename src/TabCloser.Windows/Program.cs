namespace TabCloser.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using SingleInstance instance = new("Local\\TabCloser.Windows");
        bool startedWithWindows = StartupRegistration.IsStartupLaunch(args);
        LaunchAction launchAction = LaunchPolicy.Decide(
            instance.IsPrimary,
            startedWithWindows);
        if (launchAction == LaunchAction.RequestTrayIconRestore)
        {
            instance.RequestTrayIconRestore();
            return;
        }

        if (launchAction == LaunchAction.Exit)
        {
            return;
        }

        try
        {
            using TrayApplicationContext context = new(
                instance,
                launchAction == LaunchAction.RunUsingSavedVisibility);
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
