using TabCloser.Windows.Diagnostics;

namespace TabCloser.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool diagnosticsRequested = args.Contains("--diagnostics", StringComparer.Ordinal);

        using SingleInstance instance = new("Local\\TabCloser.Windows");
        bool startedWithWindows = StartupRegistration.IsStartupLaunch(args);
        LaunchAction launchAction = LaunchPolicy.Decide(
            instance.IsPrimary,
            startedWithWindows);
        if (launchAction == LaunchAction.RequestTrayIconRestore)
        {
            instance.RequestTrayIconRestore();
            if (diagnosticsRequested)
            {
                MessageBox.Show(
                    "TabCloser is already running. Choose Exit from its tray menu, " +
                    "then launch the diagnostic build again. Logging has not started.",
                    "TabCloser diagnostics",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        if (launchAction == LaunchAction.Exit)
        {
            return;
        }

        try
        {
            RuntimeDiagnostics? diagnostics = diagnosticsRequested ? new() : null;
            using DiagnosticRecorder? recorder = diagnostics is null
                ? null
                : new DiagnosticRecorder(diagnostics);
            using TrayApplicationContext context = new(
                instance,
                launchAction == LaunchAction.RunUsingSavedVisibility,
                diagnostics);
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
