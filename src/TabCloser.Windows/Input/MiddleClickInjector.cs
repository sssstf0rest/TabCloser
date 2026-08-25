using System.Runtime.InteropServices;
using TabCloser.Core;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal static class MiddleClickInjector
{
    public static bool TryClick(
        TabTarget expectedTarget,
        ScreenPoint validatedPoint,
        long releaseMonotonicTimestampMilliseconds,
        DoubleClickConfiguration configuration,
        Func<bool> isInteractionCurrent)
    {
        if (!IsFinalStateValid(
                expectedTarget,
                validatedPoint,
                releaseMonotonicTimestampMilliseconds,
                configuration))
        {
            return false;
        }

        NativeMethods.NativeInput[] inputs = CreateMiddleClickInputs();

        if (!isInteractionCurrent() ||
            !IsFinalStateValid(
                expectedTarget,
                validatedPoint,
                releaseMonotonicTimestampMilliseconds,
                configuration) ||
            !isInteractionCurrent())
        {
            return false;
        }

        return SendPreparedMiddleClick(inputs, SendNativeInputs);
    }

    internal static NativeMethods.NativeInput[] CreateMiddleClickInputs() =>
    [
        MouseInput(NativeMethods.MouseEventMiddleDown),
        MouseInput(NativeMethods.MouseEventMiddleUp),
    ];

    internal static bool SendPreparedMiddleClick(
        NativeMethods.NativeInput[] inputs,
        Func<NativeMethods.NativeInput[], uint> sendInputs)
    {
        uint sent = sendInputs(inputs);
        if (sent == inputs.Length)
        {
            return true;
        }

        if (sent == 1)
        {
            NativeMethods.NativeInput[] recovery =
            [MouseInput(NativeMethods.MouseEventMiddleUp)];
            sendInputs(recovery);
        }

        return false;
    }

    private static uint SendNativeInputs(NativeMethods.NativeInput[] inputs) =>
        NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.NativeInput>());

    private static bool IsFinalStateValid(
        TabTarget expectedTarget,
        ScreenPoint validatedPoint,
        long releaseMonotonicTimestampMilliseconds,
        DoubleClickConfiguration configuration)
    {
        long age = Environment.TickCount64 - releaseMonotonicTimestampMilliseconds;
        if (age < 0 ||
            age > configuration.MaximumDelayMilliseconds ||
            !NativeMethods.GetCursorPos(out NativeMethods.NativePoint nativePoint))
        {
            return false;
        }

        ScreenPoint currentPoint = new(nativePoint.X, nativePoint.Y);
        nint rootAtPointer = NativeMethods.GetAncestor(
            NativeMethods.WindowFromPoint(nativePoint),
            NativeMethods.GetAncestorRoot);
        return currentPoint == validatedPoint &&
               expectedTarget.Bounds.Contains(currentPoint) &&
               rootAtPointer.ToInt64() == expectedTarget.RootWindow &&
               NativeMethods.GetForegroundWindow().ToInt64() == expectedTarget.RootWindow &&
               !NativeMethods.HasMouseButtonDown() &&
               !NativeMethods.HasModifierKeyDown();
    }

    private static NativeMethods.NativeInput MouseInput(uint flags) => new()
    {
        Type = NativeMethods.InputMouse,
        Data = new NativeMethods.NativeInputUnion
        {
            Mouse = new NativeMethods.NativeMouseInput
            {
                Flags = flags,
                ExtraInfo = NativeMethods.InjectionMarker,
            },
        },
    };
}
