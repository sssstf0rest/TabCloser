using DoubleClickCloseTab.Core;
using DoubleClickCloseTab.Windows.Interop;

namespace DoubleClickCloseTab.Windows.Input;

internal static class WindowsDoubleClickSettings
{
    public static DoubleClickConfiguration Read() => new(
        NativeMethods.GetDoubleClickTime(),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxDoubleClick)),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyDoubleClick)));
}
