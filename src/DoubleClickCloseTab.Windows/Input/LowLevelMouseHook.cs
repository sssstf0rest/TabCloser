using System.ComponentModel;
using System.Runtime.InteropServices;
using DoubleClickCloseTab.Core;
using DoubleClickCloseTab.Windows.Interop;

namespace DoubleClickCloseTab.Windows.Input;

internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly Action<MouseButtonEvent> _onMouseButtonEvent;
    private readonly NativeMethods.HookProcedure _callback;
    private nint _hook;
    private bool _trackingLeftButton;
    private ScreenPoint _leftDownPoint;
    private int _maximumTravelX;
    private int _maximumTravelY;
    private long _inputSequence;
    private long _pointerRevision;
    private int _observedLeftButtonDown;
    private bool _hasLastPointerPoint;
    private ScreenPoint _lastPointerPoint;

    public LowLevelMouseHook(Action<MouseButtonEvent> onMouseButtonEvent)
    {
        _onMouseButtonEvent = onMouseButtonEvent;
        _callback = HandleHook;
    }

    public void Start()
    {
        if (_hook != nint.Zero)
        {
            return;
        }

        nint module = NativeMethods.GetModuleHandleW(null);
        _hook = NativeMethods.SetWindowsHookExW(
            NativeMethods.WhMouseLowLevel,
            _callback,
            module,
            threadId: 0);

        if (_hook == nint.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public long CurrentInputSequence => Interlocked.Read(ref _inputSequence);

    public long CurrentPointerRevision => Interlocked.Read(ref _pointerRevision);

    public bool IsLeftButtonObservedDown =>
        Volatile.Read(ref _observedLeftButtonDown) != 0;

    public void Dispose()
    {
        if (_hook == nint.Zero)
        {
            return;
        }

        nint hook = _hook;
        if (NativeMethods.UnhookWindowsHookEx(hook))
        {
            _hook = nint.Zero;
        }

        GC.KeepAlive(_callback);
    }

    private nint HandleHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                NativeMethods.LowLevelMouseData data =
                    Marshal.PtrToStructure<NativeMethods.LowLevelMouseData>(lParam);
                int message = unchecked((int)wParam.ToInt64());
                ObservePointer(data.Point);

                if (message == NativeMethods.WmMouseMove)
                {
                    TrackMovement(data.Point);
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }

                MouseButtonEventKind? kind = Classify(message);
                if (kind is null)
                {
                    return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
                }

                bool injected =
                    (data.Flags & (NativeMethods.LowLevelMouseInjected |
                                   NativeMethods.LowLevelMouseLowerIntegrityInjected)) != 0 ||
                    data.ExtraInfo == NativeMethods.InjectionMarker;
                long inputSequence = Interlocked.Increment(ref _inputSequence);

                if (kind == MouseButtonEventKind.LeftDown)
                {
                    Volatile.Write(ref _observedLeftButtonDown, 1);
                    BeginMovementTracking(data.Point);
                }
                else if (kind == MouseButtonEventKind.LeftUp)
                {
                    Volatile.Write(ref _observedLeftButtonDown, 0);
                    TrackMovement(data.Point);
                }
                else
                {
                    _trackingLeftButton = false;
                }

                _onMouseButtonEvent(new MouseButtonEvent(
                    kind.Value,
                    new ScreenPoint(data.Point.X, data.Point.Y),
                    data.Time,
                    GetMonotonicEventTimestamp(data.Time),
                    injected,
                    NativeMethods.HasModifierKeyDown(),
                    GetRootWindow(data.Point).ToInt64(),
                    inputSequence,
                    CurrentPointerRevision,
                    _maximumTravelX,
                    _maximumTravelY));

                if (kind == MouseButtonEventKind.LeftUp)
                {
                    _trackingLeftButton = false;
                }
            }
            catch
            {
                // A global hook must never disrupt the user's normal input path.
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static MouseButtonEventKind? Classify(int message) => message switch
    {
        NativeMethods.WmLeftButtonDown => MouseButtonEventKind.LeftDown,
        NativeMethods.WmLeftButtonUp => MouseButtonEventKind.LeftUp,
        NativeMethods.WmRightButtonDown or
        NativeMethods.WmRightButtonUp or
        NativeMethods.WmMiddleButtonDown or
        NativeMethods.WmMiddleButtonUp or
        NativeMethods.WmMouseWheel or
        NativeMethods.WmMouseHorizontalWheel or
        NativeMethods.WmXButtonDown or
        NativeMethods.WmXButtonUp => MouseButtonEventKind.OtherButton,
        _ => null,
    };

    private void BeginMovementTracking(NativeMethods.NativePoint point)
    {
        _trackingLeftButton = true;
        _leftDownPoint = new ScreenPoint(point.X, point.Y);
        _maximumTravelX = 0;
        _maximumTravelY = 0;
    }

    private void TrackMovement(NativeMethods.NativePoint point)
    {
        if (!_trackingLeftButton)
        {
            return;
        }

        _maximumTravelX = Math.Max(
            _maximumTravelX,
            ClampDistance((long)point.X - _leftDownPoint.X));
        _maximumTravelY = Math.Max(
            _maximumTravelY,
            ClampDistance((long)point.Y - _leftDownPoint.Y));
    }

    private void ObservePointer(NativeMethods.NativePoint point)
    {
        ScreenPoint current = new(point.X, point.Y);
        if (_hasLastPointerPoint && current == _lastPointerPoint)
        {
            return;
        }

        _lastPointerPoint = current;
        _hasLastPointerPoint = true;
        Interlocked.Increment(ref _pointerRevision);
    }

    private static nint GetRootWindow(NativeMethods.NativePoint point)
    {
        nint window = NativeMethods.WindowFromPoint(point);
        return window == nint.Zero
            ? nint.Zero
            : NativeMethods.GetAncestor(window, NativeMethods.GetAncestorRoot);
    }

    private static long GetMonotonicEventTimestamp(uint eventTimestamp)
    {
        long now = Environment.TickCount64;
        uint age = unchecked((uint)now - eventTimestamp);
        return now - age;
    }

    private static int ClampDistance(long distance) =>
        (int)Math.Min(int.MaxValue, Math.Abs(distance));
}
