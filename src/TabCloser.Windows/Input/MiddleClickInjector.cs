using System.Runtime.InteropServices;
using TabCloser.Core;
using TabCloser.Windows.Diagnostics;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal static class MiddleClickInjector
{
    public static bool TryClick(
        TabTarget expectedTarget,
        ScreenPoint validatedPoint,
        long releaseMonotonicTimestampMilliseconds,
        DoubleClickConfiguration configuration,
        Func<bool> isInteractionCurrent,
        RuntimeDiagnostics? diagnostics = null,
        Action<InputSendOutcome>? onSendCompleted = null)
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

        return SendPreparedMiddleClick(inputs, SendNativeInputs, diagnostics, onSendCompleted);
    }

    internal static NativeMethods.NativeInput[] CreateMiddleClickInputs() =>
    [
        MouseInput(NativeMethods.MouseEventMiddleDown),
        MouseInput(NativeMethods.MouseEventMiddleUp),
    ];

    internal static bool SendPreparedMiddleClick(
        NativeMethods.NativeInput[] inputs,
        Func<NativeMethods.NativeInput[], uint> sendInputs,
        RuntimeDiagnostics? diagnostics = null,
        Action<InputSendOutcome>? onSendCompleted = null)
    {
        diagnostics?.Count(DiagnosticCounter.SendInputCalls);
        diagnostics?.Count(DiagnosticCounter.SendInputRequested, inputs.Length);
        uint sent = sendInputs(inputs);
        // Capture the P/Invoke error before any other native call can replace it.
        int? error = sent != inputs.Length
            ? Marshal.GetLastPInvokeError()
            : null;
        uint? recoveryInserted = null;
        int? recoveryError = null;
        diagnostics?.Count(DiagnosticCounter.SendInputInserted, sent);
        if (sent != inputs.Length && sent == 1)
        {
            diagnostics?.Count(DiagnosticCounter.SendInputPartialResults);
            NativeMethods.NativeInput[] recovery =
            [MouseInput(NativeMethods.MouseEventMiddleUp)];
            diagnostics?.Count(DiagnosticCounter.SendInputCalls);
            diagnostics?.Count(DiagnosticCounter.SendInputRequested, recovery.Length);
            uint recovered = sendInputs(recovery);
            recoveryError = diagnostics is not null && recovered != recovery.Length
                ? Marshal.GetLastPInvokeError()
                : null;
            recoveryInserted = recovered;
            diagnostics?.Count(DiagnosticCounter.SendInputInserted, recovered);
            if (recovered == 0)
            {
                diagnostics?.Count(DiagnosticCounter.SendInputZeroResults);
            }
        }
        else if (sent != inputs.Length && sent == 0)
        {
            diagnostics?.Count(DiagnosticCounter.SendInputZeroResults);
        }

        // Desktop probes happen only after the send and any required middle-up
        // recovery. They must never delay release of a partially inserted click.
        diagnostics?.RecordSendResult(
            (uint)inputs.Length, sent, error, recoveryInserted, recoveryError);
        onSendCompleted?.Invoke(new InputSendOutcome((uint)inputs.Length, sent, error));
        return sent == inputs.Length;
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
