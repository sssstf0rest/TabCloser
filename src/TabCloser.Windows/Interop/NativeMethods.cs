using System.Runtime.InteropServices;
using System.Text;

namespace DoubleClickCloseTab.Windows.Interop;

internal static class NativeMethods
{
    internal const int WhMouseLowLevel = 14;
    internal const int WmMouseMove = 0x0200;
    internal const int WmLeftButtonDown = 0x0201;
    internal const int WmLeftButtonUp = 0x0202;
    internal const int WmRightButtonDown = 0x0204;
    internal const int WmRightButtonUp = 0x0205;
    internal const int WmMiddleButtonDown = 0x0207;
    internal const int WmMiddleButtonUp = 0x0208;
    internal const int WmMouseWheel = 0x020A;
    internal const int WmXButtonDown = 0x020B;
    internal const int WmXButtonUp = 0x020C;
    internal const int WmMouseHorizontalWheel = 0x020E;
    internal const uint LowLevelMouseInjected = 0x00000001;
    internal const uint LowLevelMouseLowerIntegrityInjected = 0x00000002;
    internal const uint InputMouse = 0;
    internal const uint MouseEventMiddleDown = 0x0020;
    internal const uint MouseEventMiddleUp = 0x0040;
    internal const uint EventSystemDesktopSwitch = 0x0020;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const int GetAncestorRoot = 2;
    internal const int SmCxDoubleClick = 36;
    internal const int SmCyDoubleClick = 37;
    internal const int VkLeftButton = 0x01;
    internal const int VkRightButton = 0x02;
    internal const int VkMiddleButton = 0x04;
    internal const int VkXButton1 = 0x05;
    internal const int VkXButton2 = 0x06;
    internal const int VkShift = 0x10;
    internal const int VkControl = 0x11;
    internal const int VkMenu = 0x12;
    internal const int VkLeftWindows = 0x5B;
    internal const int VkRightWindows = 0x5C;
    internal const nuint InjectionMarker = 0x44434354;

    internal delegate nint HookProcedure(int code, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void WinEventProcedure(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTimeMilliseconds);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookExW(
        int hookId,
        HookProcedure callback,
        nint module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(
        nint hook,
        int code,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint module,
        WinEventProcedure callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, int flags);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out NativeRectangle rectangle);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(
        nint window,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(
        uint inputCount,
        [In] NativeInput[] inputs,
        int inputSize);

    internal static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    internal static bool HasModifierKeyDown() =>
        IsKeyDown(VkShift) ||
        IsKeyDown(VkControl) ||
        IsKeyDown(VkMenu) ||
        IsKeyDown(VkLeftWindows) ||
        IsKeyDown(VkRightWindows);

    internal static bool HasMouseButtonDown() =>
        IsKeyDown(VkLeftButton) ||
        IsKeyDown(VkRightButton) ||
        IsKeyDown(VkMiddleButton) ||
        IsKeyDown(VkXButton1) ||
        IsKeyDown(VkXButton2);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal int X;

        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRectangle
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelMouseData
    {
        internal NativePoint Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput
    {
        internal uint Type;
        internal NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct NativeInputUnion
    {
        [FieldOffset(0)]
        internal NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMouseInput
    {
        internal int DeltaX;
        internal int DeltaY;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal nuint ExtraInfo;
    }
}
