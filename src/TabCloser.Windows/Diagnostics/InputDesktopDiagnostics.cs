using System.Runtime.InteropServices;
using System.Text;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Diagnostics;

internal static class InputDesktopDiagnostics
{
    internal static InputDesktopSnapshot Capture()
    {
        uint threadId = NativeMethods.GetCurrentThreadId();
        nint sender = NativeMethods.GetThreadDesktop(threadId);
        int senderError = sender == nint.Zero ? Marshal.GetLastPInvokeError() : 0;
        nint input = NativeMethods.OpenInputDesktop(0, inherit: false, desiredAccess: 0x0001);
        int inputError = input == nint.Zero ? Marshal.GetLastPInvokeError() : 0;
        try
        {
            DesktopProbe senderProbe = ReadDesktop(sender, senderError, out string? senderName);
            DesktopProbe inputProbe = ReadDesktop(input, inputError, out string? inputName);
            nint station = NativeMethods.GetProcessWindowStation();
            int stationError = station == nint.Zero ? Marshal.GetLastPInvokeError() : 0;
            string? stationName = station == nint.Zero ? null : ReadName(station, out stationError);
            return new InputDesktopSnapshot(
                threadId,
                senderProbe,
                inputProbe,
                senderName is null || inputName is null
                    ? null
                    : string.Equals(senderName, inputName, StringComparison.OrdinalIgnoreCase),
                CategorizeName(stationName),
                stationError);
        }
        finally
        {
            // Only OpenInputDesktop returns an owned handle. Thread/station
            // handles are borrowed and must not be closed or changed here.
            if (input != nint.Zero)
            {
                NativeMethods.CloseDesktop(input);
            }
        }
    }

    private static DesktopProbe ReadDesktop(nint handle, int handleError, out string? name)
    {
        name = null;
        if (handle == nint.Zero)
        {
            return new DesktopProbe("Unavailable", null, handleError, null, null);
        }

        name = ReadName(handle, out int nameError);
        bool flagAvailable = NativeMethods.GetUserObjectInputFlag(
            handle, index: 6, out int receivesInput, sizeof(int), out _);
        int flagError = flagAvailable ? 0 : Marshal.GetLastPInvokeError();
        return new DesktopProbe(
            CategorizeName(name),
            flagAvailable ? receivesInput != 0 : null,
            0,
            nameError,
            flagError);
    }

    private static string? ReadName(nint handle, out int error)
    {
        StringBuilder name = new(256);
        bool success = NativeMethods.GetUserObjectName(
            handle, index: 2, name, (uint)name.Capacity * sizeof(char), out _);
        error = success ? 0 : Marshal.GetLastPInvokeError();
        return success ? name.ToString() : null;
    }

    internal static string CategorizeName(string? name) => name?.ToLowerInvariant() switch
    {
        null => "Unavailable",
        "default" => "Default",
        "winlogon" => "Winlogon",
        "winsta0" => "WinSta0",
        _ => "Other",
    };
}

internal sealed record DesktopProbe(
    string Category,
    bool? ReceivesInput,
    int HandleError,
    int? NameError,
    int? InputFlagError);

internal sealed record InputDesktopSnapshot(
    uint SenderNativeThreadId,
    DesktopProbe SenderDesktop,
    DesktopProbe InputDesktop,
    bool? SenderNameMatchesInputDesktop,
    string WindowStationCategory,
    int WindowStationError);

internal sealed record InputSendReport(
    DateTimeOffset Utc,
    uint Requested,
    uint Inserted,
    int? Win32Error,
    uint? RecoveryInserted,
    int? RecoveryWin32Error,
    InputDesktopSnapshot? DesktopAfterSend,
    string? DesktopProbeErrorType,
    int WorkerGeneration);
