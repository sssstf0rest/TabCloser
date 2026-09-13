using TabCloser.Windows.Diagnostics;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal enum WorkerRestartResult
{
    Restarted,
    Unavailable,
    Busy,
    TimedOut,
    Failed,
}

// Owns only the worker thread; it never installs hooks or sends input. The caller
// must enforce the recovery budget before requesting an automatic restart.
internal sealed class TabCloseWorker : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<CancellationToken> _run;
    private readonly Action _invalidate;
    private readonly Action _resetAfterStop;
    private readonly RuntimeDiagnostics? _diagnostics;
    private readonly TimeSpan _stopTimeout;
    private CancellationTokenSource _cancellation = new();
    private Thread? _thread;
    private int _generation;
    private int _suspended;
    private bool _restartInProgress;
    private bool _disposed;

    internal TabCloseWorker(
        Action<CancellationToken> run,
        Action invalidate,
        Action resetAfterStop,
        RuntimeDiagnostics? diagnostics,
        TimeSpan? stopTimeout = null)
    {
        _run = run;
        _invalidate = invalidate;
        _resetAfterStop = resetAfterStop;
        _diagnostics = diagnostics;
        _stopTimeout = stopTimeout ?? TimeSpan.FromSeconds(2);
    }

    internal bool IsSuspended => Volatile.Read(ref _suspended) != 0;

    internal void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null)
            {
                StartThread();
            }
        }
    }

    internal Task<WorkerRestartResult> RestartForDiagnosticsAsync() =>
        _diagnostics is null
            ? Task.FromResult(WorkerRestartResult.Unavailable)
            : RestartAsync("Restart");

    internal Task<WorkerRestartResult> RestartAutomaticallyAsync() =>
        RestartAsync("AutomaticRestart");

    private async Task<WorkerRestartResult> RestartAsync(string eventPrefix)
    {
        Thread previous;
        lock (_gate)
        {
            if (_disposed || _thread is null)
            {
                return WorkerRestartResult.Unavailable;
            }

            if (_restartInProgress)
            {
                return WorkerRestartResult.Busy;
            }

            _restartInProgress = true;
            Volatile.Write(ref _suspended, 1);
            _invalidate();
            previous = _thread;
            Record(eventPrefix + "Requested", previous);
            _cancellation.Cancel();
        }

        try
        {
            // Never block the tray/hook message loop while waiting for UIA.
            bool stopped = await Task.Run(() => previous.Join(_stopTimeout));
            lock (_gate)
            {
                if (_disposed)
                {
                    return WorkerRestartResult.Unavailable;
                }

                if (!stopped)
                {
                    Record(eventPrefix + "TimedOut", previous);
                    return WorkerRestartResult.TimedOut;
                }

                // Join proves that no old worker can touch this state again.
                // Input remains gated until queued events and gesture state are cleared.
                _resetAfterStop();
                _cancellation.Dispose();
                _cancellation = new CancellationTokenSource();
                // Open the input gate before starting, so an immediately failing
                // replacement cannot have its suspended state overwritten here.
                Volatile.Write(ref _suspended, 0);
                StartThread();
                Record(eventPrefix + "Completed", _thread!);
                return WorkerRestartResult.Restarted;
            }
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _suspended, 1);
            _diagnostics?.Error("WorkerRestart", exception);
            lock (_gate)
            {
                Record(eventPrefix + "Failed", previous);
            }

            return WorkerRestartResult.Failed;
        }
        finally
        {
            lock (_gate)
            {
                _restartInProgress = false;
            }
        }
    }

    private void StartThread()
    {
        CancellationToken token = _cancellation.Token;
        int generation = ++_generation;
        _thread = new Thread(() =>
        {
            _diagnostics?.WorkerStarted(generation);
            try
            {
                _run(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Cooperative stop requested by shutdown or the diagnostic action.
            }
            catch (Exception exception)
            {
                Volatile.Write(ref _suspended, 1);
                _diagnostics?.Count(DiagnosticCounter.WorkerFailures);
                _diagnostics?.Error("Worker", exception);
                _diagnostics?.Stage("Faulted");
            }
            finally
            {
                _diagnostics?.RecordWorkerLifecycle("Stopped", generation,
                    Environment.CurrentManagedThreadId, NativeMethods.GetCurrentThreadId());
            }
        })
        {
            IsBackground = true,
            Name = "Chrome tab hit-test worker",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    private void Record(string eventName, Thread thread) =>
        _diagnostics?.RecordWorkerLifecycle(eventName, _generation, thread.ManagedThreadId);

    public void Dispose()
    {
        Thread? thread;
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Volatile.Write(ref _suspended, 1);
            _invalidate();
            cancellation = _cancellation;
            cancellation.Cancel();
            thread = _thread;
        }

        // A timed-out worker can still use its token. Do not dispose its source
        // underneath it; the background thread cannot keep the app alive.
        if (thread is null || !thread.IsAlive || thread.Join(_stopTimeout))
        {
            cancellation.Dispose();
        }
    }
}
