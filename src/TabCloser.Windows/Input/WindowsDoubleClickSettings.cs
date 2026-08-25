using TabCloser.Core;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal static class WindowsDoubleClickSettings
{
    public static DoubleClickConfiguration Read() => new(
        NativeMethods.GetDoubleClickTime(),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCxDoubleClick)),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SmCyDoubleClick)));
}
