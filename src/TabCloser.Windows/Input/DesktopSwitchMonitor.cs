using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickCloseTab.Windows.Interop;

namespace DoubleClickCloseTab.Windows.Input;

internal sealed class DesktopSwitchMonitor : IDisposable
{
    private readonly Action _onDesktopSwitch;
    private readonly NativeMethods.WinEventProcedure _callback;
    private nint _hook;

    public DesktopSwitchMonitor(Action onDesktopSwitch)
    {
        _onDesktopSwitch = onDesktopSwitch;
        _callback = HandleWinEvent;
    }

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EventSystemDesktopSwitch,
            NativeMethods.EventSystemDesktopSwitch,
            module: nint.Zero,
            _callback,
            processId: 0,
            threadId: 0,
            NativeMethods.WinEventOutOfContext);

        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        nint hook = _hook;
        if (NativeMethods.UnhookWinEvent(hook))
        {
            _hook = nint.Zero;
        }

        GC.KeepAlive(_callback);
    }

    private void HandleWinEvent(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTimeMilliseconds)
    {
        try
        {
            _onDesktopSwitch();
        }
        catch
        {
            // A desktop-switch observer must not disrupt the system event path.
        }
    }
}
