using System.Threading.Channels;
using TabCloser.Core;
using TabCloser.Windows.Browser;
using TabCloser.Windows.Diagnostics;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal sealed class TabCloseService : IDisposable
{
    private readonly object _stateGate = new();
    private readonly RuntimeDiagnostics? _diagnostics;
    private readonly Channel<QueuedMouseEvent> _mouseEvents;
    private readonly DoubleClickDetector _detector = new();
    private readonly MouseClickAssembler _assembler = new();
    private TabTarget? _pendingDownTarget;
    private long _pendingDownInputSequence;
    private readonly LowLevelMouseHook _hook;
    private readonly DesktopSwitchMonitor _desktopSwitchMonitor;
    private readonly TabCloseWorker _worker;
    private readonly InputRecoveryPolicy _recoveryPolicy = new();
    private int _overflowed;
    private int _resetAssemblerRequested;
    private long _interactionGeneration;
    private bool _enabled = true;
    private bool _started;
    private bool _disposed;

    public TabCloseService(RuntimeDiagnostics? diagnostics = null)
    {
        _diagnostics = diagnostics;
        _mouseEvents = Channel.CreateBounded<QueuedMouseEvent>(
            new BoundedChannelOptions(capacity: 32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        _hook = new LowLevelMouseHook(QueueMouseEvent, diagnostics);
        _desktopSwitchMonitor = new DesktopSwitchMonitor(() =>
        {
            _diagnostics?.Count(DiagnosticCounter.DesktopSwitches);
            InvalidateInteraction();
        });
        _worker = new TabCloseWorker(WorkerMain, InvalidateInteraction,
            ResetAfterWorkerStop, diagnostics);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _worker.Start();
        try
        {
            _desktopSwitchMonitor.Start();
            _hook.Start();
            _started = true;
        }
        catch
        {
            _hook.Dispose();
            _desktopSwitchMonitor.Dispose();
            _mouseEvents.Writer.TryComplete();
            _worker.Dispose();
            throw;
        }
    }

    public void SetEnabled(bool enabled)
    {
        _diagnostics?.SetEnabled(enabled);
        lock (_stateGate)
        {
            _enabled = enabled;
            Interlocked.Increment(ref _interactionGeneration);
            _detector.Reset();
            _recoveryPolicy.ClearEvidence();
        }

        Interlocked.Exchange(ref _resetAssemblerRequested, 1);
    }

    internal Task<WorkerRestartResult> RestartWorkerForDiagnosticsAsync() =>
        _worker.RestartForDiagnosticsAsync();

    // Called by the tray timer, never by the sending worker: a worker must not
    // wait for its own termination. No timer or retry is scheduled after failure.
    internal async Task<WorkerRestartResult?> RecoverWorkerIfRequestedAsync()
    {
        lock (_stateGate)
        {
            if (_disposed || !_started || !_enabled ||
                !_recoveryPolicy.TryTakeRequest(Environment.TickCount64))
            {
                return null;
            }
        }

        return await _worker.RestartAutomaticallyAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_stateGate)
        {
            _enabled = false;
            Interlocked.Increment(ref _interactionGeneration);
            _detector.Reset();
        }

        Interlocked.Exchange(ref _resetAssemblerRequested, 1);
        _hook.Dispose();
        _desktopSwitchMonitor.Dispose();
        _mouseEvents.Writer.TryComplete();
        _worker.Dispose();
    }

    private void WorkerMain(CancellationToken cancellation)
    {
        try
        {
            _diagnostics?.Stage("UIA.Initializing");
            ChromeTabHitTester hitTester = new(_diagnostics);
            _diagnostics?.CaptureWorkerInitialDesktop();
            _diagnostics?.Stage("Idle");

            while (!cancellation.IsCancellationRequested && _mouseEvents.Reader
                .WaitToReadAsync(cancellation)
                .AsTask()
                .GetAwaiter()
                .GetResult())
            {
                if (FlushAfterOverflow())
                {
                    continue;
                }

                while (_mouseEvents.Reader.TryRead(out QueuedMouseEvent queuedEvent))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (FlushAfterOverflow())
                    {
                        break;
                    }

                    ApplyRequestedReset();
                    ProcessSafely(queuedEvent, hitTester);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _diagnostics?.Stage("Stopped");
            // Application shutdown or a manual diagnostic worker restart.
        }
        catch (Exception exception)
        {
            _diagnostics?.Count(DiagnosticCounter.WorkerFailures);
            _diagnostics?.Error("Worker", exception);
            _diagnostics?.Stage("Faulted");
            // Fail closed if the accessibility worker cannot continue.
        }
    }

    private void ProcessSafely(
        QueuedMouseEvent queuedEvent,
        ChromeTabHitTester hitTester)
    {
        _diagnostics?.Count(DiagnosticCounter.ProcessStarted);
        _diagnostics?.Stage("Processing");
        try
        {
            Process(queuedEvent, hitTester);
        }
        catch (Exception exception)
        {
            _diagnostics?.Count(DiagnosticCounter.ProcessErrors);
            _diagnostics?.Error("Process", exception);
            ResetAllGestureState();
        }
        finally
        {
            _diagnostics?.Count(DiagnosticCounter.ProcessCompleted);
            _diagnostics?.Stage("Idle");
        }
    }

    private void Process(
        QueuedMouseEvent queuedEvent,
        ChromeTabHitTester hitTester)
    {
        if (!IsEventCurrent(queuedEvent.InteractionGeneration))
        {
            ResetAllGestureState();
            return;
        }

        MouseButtonEvent mouseEvent = queuedEvent.MouseEvent;
        ClickAssemblyResult assembly = _assembler.Register(mouseEvent);
        if (assembly.ResetSequence)
        {
            ClearPendingDownTarget();
            ResetDetector();
        }

        if (mouseEvent.Kind == MouseButtonEventKind.LeftDown)
        {
            CaptureDownTarget(queuedEvent, hitTester);
            return;
        }

        if (assembly.Click is not MouseClick click)
        {
            return;
        }

        DoubleClickConfiguration configuration = WindowsDoubleClickSettings.Read();
        if (!click.IsEligible(configuration) ||
            !IsEventCurrent(queuedEvent.InteractionGeneration))
        {
            ClearPendingDownTarget();
            ResetDetector();
            return;
        }

        TabTarget? downTarget = TakePendingDownTarget(click.DownInputSequence);
        TabTarget? hit = HitTestCompleteClick(hitTester, click, downTarget);
        bool completed;

        lock (_stateGate)
        {
            if (!_enabled ||
                Interlocked.Read(ref _interactionGeneration) !=
                queuedEvent.InteractionGeneration)
            {
                _detector.Reset();
                return;
            }

            completed = _detector.Register(click, hit, configuration);
        }

        if (!completed || hit is null)
        {
            return;
        }

        _diagnostics?.Count(DiagnosticCounter.DoubleClicksRecognized);

        long processingAge = Environment.TickCount64 -
            click.UpMonotonicTimestampMilliseconds;
        if (processingAge < 0 ||
            processingAge > configuration.MaximumDelayMilliseconds ||
            !IsInteractionCurrent(
                queuedEvent.InteractionGeneration,
                click.InputSequence,
                click.PointerRevision) ||
            !NativeMethods.GetCursorPos(out NativeMethods.NativePoint nativePoint))
        {
            return;
        }

        ScreenPoint currentPoint = new(nativePoint.X, nativePoint.Y);
        if (!configuration.Contains(click.UpPoint, currentPoint) ||
            !hit.Bounds.Contains(currentPoint))
        {
            return;
        }

        TabTarget? currentHit = hitTester.HitTest(currentPoint);
        if (currentHit is null ||
            currentHit.RootWindow != hit.RootWindow ||
            !string.Equals(
                currentHit.Identity,
                hit.Identity,
                StringComparison.Ordinal) ||
            !currentHit.Bounds.Contains(currentPoint))
        {
            return;
        }

        if (!IsInteractionCurrent(
                queuedEvent.InteractionGeneration,
                click.InputSequence,
                click.PointerRevision))
        {
            return;
        }

        _diagnostics?.Count(DiagnosticCounter.CloseAttempts);
        _diagnostics?.Stage("Injecting");
        bool closed = MiddleClickInjector.TryClick(
            currentHit,
            currentPoint,
            click.UpMonotonicTimestampMilliseconds,
            configuration,
            () => IsInteractionCurrent(
                queuedEvent.InteractionGeneration,
                click.InputSequence,
                click.PointerRevision),
            _diagnostics,
            outcome => ObserveSendResult(outcome, queuedEvent.InteractionGeneration));
        if (closed)
        {
            _diagnostics?.Count(DiagnosticCounter.CloseBatchesInserted);
        }
    }

    private void QueueMouseEvent(MouseButtonEvent mouseEvent)
    {
        long interactionGeneration = Interlocked.Read(ref _interactionGeneration);
        if (_worker.IsSuspended)
        {
            return;
        }

        if (!_mouseEvents.Writer.TryWrite(new QueuedMouseEvent(
                mouseEvent,
                interactionGeneration)))
        {
            _diagnostics?.Count(DiagnosticCounter.QueueOverflows);
            if (Interlocked.Exchange(ref _overflowed, 1) == 0)
            {
                InvalidateInteraction();
            }
        }
        else
        {
            _diagnostics?.Count(DiagnosticCounter.QueuedEvents);
        }
    }

    private bool FlushAfterOverflow()
    {
        if (Interlocked.Exchange(ref _overflowed, 0) == 0)
        {
            return false;
        }

        ResetAllGestureState();
        while (_mouseEvents.Reader.TryRead(out _))
        {
        }

        return true;
    }

    private void ApplyRequestedReset()
    {
        if (Interlocked.Exchange(ref _resetAssemblerRequested, 0) == 0)
        {
            return;
        }

        ResetAllGestureState();
    }

    private bool ReadEnabled()
    {
        lock (_stateGate)
        {
            return _enabled;
        }
    }

    private bool IsEventCurrent(long expectedInteractionGeneration)
    {
        if (_worker.IsSuspended || Volatile.Read(ref _overflowed) != 0 ||
            Interlocked.Read(ref _interactionGeneration) != expectedInteractionGeneration ||
            !ReadEnabled())
        {
            return false;
        }

        return !_worker.IsSuspended && Volatile.Read(ref _overflowed) == 0 &&
               Interlocked.Read(ref _interactionGeneration) ==
               expectedInteractionGeneration;
    }

    private bool IsInteractionCurrent(
        long expectedInteractionGeneration,
        long expectedInputSequence,
        long expectedPointerRevision)
    {
        if (!IsEventCurrent(expectedInteractionGeneration) ||
            _hook.CurrentInputSequence != expectedInputSequence ||
            _hook.CurrentPointerRevision != expectedPointerRevision)
        {
            return false;
        }

        return IsEventCurrent(expectedInteractionGeneration) &&
               _hook.CurrentInputSequence == expectedInputSequence &&
               _hook.CurrentPointerRevision == expectedPointerRevision;
    }

    private void InvalidateInteraction()
    {
        Interlocked.Increment(ref _interactionGeneration);
        Interlocked.Exchange(ref _resetAssemblerRequested, 1);
        _recoveryPolicy.ClearEvidence();
    }

    private void ObserveSendResult(InputSendOutcome outcome, long generation)
    {
        lock (_stateGate)
        {
            if (_enabled && !_disposed && generation == Interlocked.Read(ref _interactionGeneration))
            {
                _recoveryPolicy.Observe(outcome, Environment.TickCount64);
            }
            else
            {
                _recoveryPolicy.ClearEvidence();
            }
        }
    }

    private void ResetAfterWorkerStop()
    {
        // Called only after Join, with input suspended. Never replay a queued
        // gesture or carry a half-click across worker generations.
        while (_mouseEvents.Reader.TryRead(out _))
        {
        }

        Interlocked.Exchange(ref _overflowed, 0);
        Interlocked.Exchange(ref _resetAssemblerRequested, 0);
        ResetAllGestureState();
        InvalidateInteraction();
    }

    private void CaptureDownTarget(
        QueuedMouseEvent queuedEvent,
        ChromeTabHitTester hitTester)
    {
        ClearPendingDownTarget();
        MouseButtonEvent mouseEvent = queuedEvent.MouseEvent;
        DoubleClickConfiguration configuration = WindowsDoubleClickSettings.Read();
        if (mouseEvent.IsInjected ||
            mouseEvent.HasModifiers ||
            mouseEvent.RootWindow == 0 ||
            !IsLeftPressCurrent(
                queuedEvent.InteractionGeneration,
                mouseEvent,
                configuration))
        {
            return;
        }

        TabTarget? target = hitTester.HitTest(mouseEvent.Point);
        if (target is null ||
            target.RootWindow != mouseEvent.RootWindow ||
            !IsLeftPressCurrent(
                queuedEvent.InteractionGeneration,
                mouseEvent,
                configuration))
        {
            return;
        }

        _pendingDownTarget = target;
        _pendingDownInputSequence = mouseEvent.InputSequence;
    }

    private bool IsLeftPressCurrent(
        long expectedInteractionGeneration,
        MouseButtonEvent mouseEvent,
        DoubleClickConfiguration configuration)
    {
        long age = Environment.TickCount64 -
            mouseEvent.MonotonicTimestampMilliseconds;
        if (age < 0 ||
            age > configuration.MaximumDelayMilliseconds ||
            !IsEventCurrent(expectedInteractionGeneration) ||
            _hook.CurrentInputSequence != mouseEvent.InputSequence ||
            !_hook.IsLeftButtonObservedDown ||
            NativeMethods.HasModifierKeyDown())
        {
            return false;
        }

        nint rootAtDownPoint = NativeMethods.GetAncestor(
            NativeMethods.WindowFromPoint(new NativeMethods.NativePoint(
                mouseEvent.Point.X,
                mouseEvent.Point.Y)),
            NativeMethods.GetAncestorRoot);
        if (rootAtDownPoint.ToInt64() != mouseEvent.RootWindow)
        {
            return false;
        }

        return IsEventCurrent(expectedInteractionGeneration) &&
               _hook.CurrentInputSequence == mouseEvent.InputSequence &&
               _hook.IsLeftButtonObservedDown &&
               !NativeMethods.HasModifierKeyDown();
    }

    private TabTarget? TakePendingDownTarget(long expectedInputSequence)
    {
        TabTarget? target = _pendingDownInputSequence == expectedInputSequence
            ? _pendingDownTarget
            : null;
        ClearPendingDownTarget();
        return target;
    }

    private void ClearPendingDownTarget()
    {
        _pendingDownTarget = null;
        _pendingDownInputSequence = 0;
    }

    private void ResetAllGestureState()
    {
        _assembler.Reset();
        ClearPendingDownTarget();
        ResetDetector();
    }

    private void ResetDetector()
    {
        lock (_stateGate)
        {
            _detector.Reset();
        }
    }

    private static TabTarget? HitTestCompleteClick(
        ChromeTabHitTester hitTester,
        MouseClick click,
        TabTarget? downTarget)
    {
        if (downTarget is null ||
            click.DownRootWindow == 0 ||
            click.DownRootWindow != click.UpRootWindow)
        {
            return null;
        }

        if (downTarget.RootWindow != click.DownRootWindow)
        {
            return null;
        }

        TabTarget? upTarget = hitTester.HitTest(click.UpPoint);
        return upTarget is not null &&
               upTarget.RootWindow == downTarget.RootWindow &&
               string.Equals(
                   upTarget.Identity,
                   downTarget.Identity,
                   StringComparison.Ordinal)
            ? upTarget
            : null;
    }

    private readonly record struct QueuedMouseEvent(
        MouseButtonEvent MouseEvent,
        long InteractionGeneration);
}
