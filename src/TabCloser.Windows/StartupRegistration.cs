using Microsoft.Win32;

namespace TabCloser.Windows;

internal static class StartupRegistration
{
    internal const string StartupArgument = "--startup";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TabCloser";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        string? command = key?.GetValue(ValueName) as string;
        return IsCommandForExecutable(command, GetExecutablePath());
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (!enabled)
        {
            string executablePath = GetExecutablePath();
            string? command = key.GetValue(ValueName) as string;
            if (IsCommandForExecutable(command, executablePath))
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return;
        }

        key.SetValue(
            ValueName,
            BuildCommand(GetExecutablePath()),
            RegistryValueKind.String);
    }

    public static bool RefreshIfEnabled()
    {
        string executablePath = GetExecutablePath();
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
            RunKeyPath,
            writable: true);
        string? command = key?.GetValue(ValueName) as string;
        if (!IsCommandForExecutable(command, executablePath))
        {
            return false;
        }

        string currentCommand = BuildCommand(executablePath);
        if (!string.Equals(command, currentCommand, StringComparison.Ordinal))
        {
            key!.SetValue(ValueName, currentCommand, RegistryValueKind.String);
        }

        return true;
    }

    internal static bool IsStartupLaunch(IEnumerable<string> arguments)
    {
        return arguments.Any(argument =>
            string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));
    }

    internal static string BuildCommand(string executablePath)
    {
        return $"\"{executablePath}\" {StartupArgument}";
    }

    internal static bool IsCommandForExecutable(string? command, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        string trimmedCommand = command.Trim();
        return string.Equals(
                   trimmedCommand,
                   $"\"{executablePath}\"",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   trimmedCommand,
                   BuildCommand(executablePath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExecutablePath()
    {
        return Environment.ProcessPath ??
            throw new InvalidOperationException("The executable path is unavailable.");
    }
}
