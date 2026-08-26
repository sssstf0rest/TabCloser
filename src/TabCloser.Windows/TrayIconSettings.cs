using Microsoft.Win32;

namespace TabCloser.Windows;

internal static class TrayIconSettings
{
    private const string KeyPath = @"Software\TabCloser";
    private const string HiddenValueName = "TrayIconHidden";

    public static bool IsHidden()
    {
        return IsHidden(KeyPath);
    }

    public static void SetHidden(bool hidden)
    {
        SetHidden(hidden, KeyPath);
    }

    internal static bool IsHidden(string keyPath)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(HiddenValueName) is int value && value != 0;
    }

    internal static void SetHidden(bool hidden, string keyPath)
    {
        if (!hidden)
        {
            using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(
                keyPath,
                writable: true);
            existingKey?.DeleteValue(HiddenValueName, throwOnMissingValue: false);
            return;
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(
            HiddenValueName,
            1,
            RegistryValueKind.DWord);
    }
}
