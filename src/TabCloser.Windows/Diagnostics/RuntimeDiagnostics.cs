using System.Collections.Concurrent;

namespace TabCloser.Windows.Diagnostics;

internal enum DiagnosticCounter
{
    HookInstalled,
    HookEntered,
    HookReturned,
    HookErrors,
    PhysicalButtonEvents,
    InjectedButtonEvents,
    QueuedEvents,
    QueueOverflows,
    ProcessStarted,
    ProcessCompleted,
    ProcessErrors,
    WorkerFailures,
    DesktopSwitches,
    EnabledChanges,
    HitTests,
    HitTestsCompleted,
    HitTestsAccepted,
    HitTestErrors,
    DoubleClicksRecognized,
    CloseAttempts,
    CloseBatchesInserted,
    SendInputCalls,
    SendInputRequested,
    SendInputInserted,
    SendInputZeroResults,
    SendInputPartialResults,
    SendReportsDropped,
    WorkerReportsDropped,
}

// Hook callbacks only update in-memory counters. Desktop probes run on the
// sending worker after injection; file I/O belongs to the separate recorder.
internal sealed class RuntimeDiagnostics
{
    private readonly long[] _counters = new long[Enum.GetValues<DiagnosticCounter>().Length];
    private long _lastHookEntry;
    private long _lastHookReturn;
    private long _lastUiPulse;
    private long _maximumHookDuration;
    private long _stageChangedAt = Environment.TickCount64;
    private string _workerStage = "NotStarted";
    private string? _lastErrorType;
    private string? _lastErrorSource;
    private int _enabled = 1;
    private readonly ConcurrentQueue<InputSendReport> _sendReports = new();
    private int _sendReportCount;
    private InputDesktopSnapshot? _workerInitialDesktop;
    private readonly ConcurrentQueue<WorkerLifecycleReport> _workerReports = new();
    private int _workerReportCount;
    private int _workerGeneration;

    internal void WorkerStarted(int generation)
    {
        Volatile.Write(ref _workerGeneration, generation);
        Volatile.Write(ref _workerInitialDesktop, null);
        RecordWorkerLifecycle("Started", generation, Environment.CurrentManagedThreadId,
            Interop.NativeMethods.GetCurrentThreadId());
    }

    internal void RecordWorkerLifecycle(
        string eventName, int generation, int managedThreadId, uint? nativeThreadId = null)
    {
        if (Interlocked.Increment(ref _workerReportCount) > 128)
        {
            Interlocked.Decrement(ref _workerReportCount);
            Count(DiagnosticCounter.WorkerReportsDropped);
            return;
        }

        _workerReports.Enqueue(new WorkerLifecycleReport(
            DateTimeOffset.UtcNow, eventName, generation, managedThreadId, nativeThreadId));
    }

    internal IReadOnlyList<WorkerLifecycleReport> DrainWorkerReports()
    {
        List<WorkerLifecycleReport> reports = [];
        while (reports.Count < 128 && _workerReports.TryDequeue(out WorkerLifecycleReport? report))
        {
            Interlocked.Decrement(ref _workerReportCount);
            reports.Add(report);
        }

        return reports;
    }

    internal void CaptureWorkerInitialDesktop()
    {
        Stage("Diagnostic.InitialDesktopProbe");
        try
        {
            Volatile.Write(ref _workerInitialDesktop, InputDesktopDiagnostics.Capture());
        }
        catch (Exception exception)
        {
            Error("InitialDesktopProbe", exception);
        }
    }

    internal void RecordSendResult(
        uint requested, uint inserted, int? error, uint? recoveryInserted, int? recoveryError)
    {
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        Stage("Diagnostic.DesktopProbe");
        InputDesktopSnapshot? desktop = null;
        string? probeError = null;
        try
        {
            desktop = InputDesktopDiagnostics.Capture();
        }
        catch (Exception exception)
        {
            probeError = exception.GetType().Name;
        }
        finally
        {
            Stage("Processing");
        }

        // Bounded, in-memory handoff: a stalled logger cannot grow this forever.
        if (Interlocked.Increment(ref _sendReportCount) > 128)
        {
            Interlocked.Decrement(ref _sendReportCount);
            Count(DiagnosticCounter.SendReportsDropped);
            return;
        }

        _sendReports.Enqueue(new InputSendReport(
            utc, requested, inserted, error, recoveryInserted, recoveryError, desktop, probeError,
            Volatile.Read(ref _workerGeneration)));
    }

    internal IReadOnlyList<InputSendReport> DrainSendReports()
    {
        List<InputSendReport> reports = [];
        while (reports.Count < 128 && _sendReports.TryDequeue(out InputSendReport? report))
        {
            Interlocked.Decrement(ref _sendReportCount);
            reports.Add(report);
        }

        return reports;
    }

    internal void Count(DiagnosticCounter counter, long amount = 1) =>
        Interlocked.Add(ref _counters[(int)counter], amount);

    internal void HookEntered()
    {
        Count(DiagnosticCounter.HookEntered);
        Interlocked.Exchange(ref _lastHookEntry, Environment.TickCount64);
    }

    internal void HookReturned(long startedAt)
    {
        long now = Environment.TickCount64;
        Count(DiagnosticCounter.HookReturned);
        Interlocked.Exchange(ref _lastHookReturn, now);
        long duration = now - startedAt;
        long maximum = Interlocked.Read(ref _maximumHookDuration);
        while (duration > maximum)
        {
            long previous = Interlocked.CompareExchange(
                ref _maximumHookDuration, duration, maximum);
            if (previous == maximum)
            {
                break;
            }

            maximum = previous;
        }
    }

    internal void UiPulse() =>
        Interlocked.Exchange(ref _lastUiPulse, Environment.TickCount64);

    internal void SetEnabled(bool enabled)
    {
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
        Count(DiagnosticCounter.EnabledChanges);
    }

    internal void Stage(string stage)
    {
        Volatile.Write(ref _workerStage, stage);
        Interlocked.Exchange(ref _stageChangedAt, Environment.TickCount64);
    }

    internal void Error(string source, Exception exception)
    {
        // Never retain exception messages/stacks: they may contain user data.
        Volatile.Write(ref _lastErrorSource, source);
        Volatile.Write(ref _lastErrorType, exception.GetType().Name);
    }

    internal DiagnosticSnapshot Snapshot()
    {
        long now = Environment.TickCount64;
        Dictionary<string, long> counters = [];
        foreach (DiagnosticCounter counter in Enum.GetValues<DiagnosticCounter>())
        {
            counters.Add(counter.ToString(), Interlocked.Read(ref _counters[(int)counter]));
        }

        return new DiagnosticSnapshot(
            DateTimeOffset.UtcNow,
            now,
            Volatile.Read(ref _enabled) != 0,
            Volatile.Read(ref _workerStage),
            now - Interlocked.Read(ref _stageChangedAt),
            Age(now, Interlocked.Read(ref _lastHookEntry)),
            Age(now, Interlocked.Read(ref _lastHookReturn)),
            Age(now, Interlocked.Read(ref _lastUiPulse)),
            Interlocked.Read(ref _maximumHookDuration),
            Volatile.Read(ref _lastErrorSource),
            Volatile.Read(ref _lastErrorType),
            Volatile.Read(ref _workerInitialDesktop),
            Volatile.Read(ref _workerGeneration),
            counters);
    }

    private static long? Age(long now, long timestamp) =>
        timestamp == 0 ? null : now - timestamp;
}

internal sealed record DiagnosticSnapshot(
    DateTimeOffset Utc,
    long UptimeMilliseconds,
    bool Enabled,
    string WorkerStage,
    long WorkerStageAgeMilliseconds,
    long? LastHookEntryAgeMilliseconds,
    long? LastHookReturnAgeMilliseconds,
    long? LastUiPulseAgeMilliseconds,
    long MaximumHookDurationMilliseconds,
    string? LastErrorSource,
    string? LastErrorType,
    InputDesktopSnapshot? WorkerInitialDesktop,
    int WorkerGeneration,
    IReadOnlyDictionary<string, long> Counters);

internal sealed record WorkerLifecycleReport(
    DateTimeOffset Utc,
    string Event,
    int Generation,
    int ManagedThreadId,
    uint? NativeThreadId);
