using System.Threading.Channels;
using TabCloser.Core;
using TabCloser.Windows.Browser;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Input;

internal sealed class TabCloseService : IDisposable
{
    private readonly object _stateGate = new();
    private readonly Channel<QueuedMouseEvent> _mouseEvents;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly DoubleClickDetector _detector = new();
    private readonly MouseClickAssembler _assembler = new();
    private TabTarget? _pendingDownTarget;
    private long _pendingDownInputSequence;
    private readonly LowLevelMouseHook _hook;
    private readonly DesktopSwitchMonitor _desktopSwitchMonitor;
    private readonly Thread _worker;
    private readonly DeferredRequestWorker<DeferredEdgeCloseRequest> _edgeWorker;
    private int _overflowed;
    private int _resetAssemblerRequested;
    private long _interactionGeneration;
    private bool _edgeEnabled;
    private bool _enabled = true;
    private bool _started;
    private bool _disposed;

    public TabCloseService()
    {
        _mouseEvents = Channel.CreateBounded<QueuedMouseEvent>(
            new BoundedChannelOptions(capacity: 32)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        _hook = new LowLevelMouseHook(QueueMouseEvent);
        _desktopSwitchMonitor = new DesktopSwitchMonitor(InvalidateInteraction);
        _worker = new Thread(WorkerMain)
        {
            IsBackground = true,
            Name = "Browser tab hit-test worker",
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _edgeWorker = new DeferredRequestWorker<DeferredEdgeCloseRequest>(
            _cancellation.Token,
            BrowserGesturePolicy.EdgeNativeCloseSettlingMilliseconds,
            CreateEdgeRequestProcessor,
            "Edge tab close settling worker");
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        try
        {
            _worker.Start();
            _edgeWorker.Start();
            _desktopSwitchMonitor.Start();
            _hook.Start();
            _started = true;
        }
        catch
        {
            _hook.Dispose();
            _desktopSwitchMonitor.Dispose();
            _cancellation.Cancel();
            _mouseEvents.Writer.TryComplete();
            _edgeWorker.Complete();
            JoinIfStarted(_worker);
            _edgeWorker.Join(TimeSpan.FromSeconds(2));
            throw;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_stateGate)
        {
            _enabled = enabled;
            Interlocked.Increment(ref _interactionGeneration);
            _detector.Reset();
        }

        Interlocked.Exchange(ref _resetAssemblerRequested, 1);
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
        _cancellation.Cancel();
        _mouseEvents.Writer.TryComplete();
        _edgeWorker.Complete();

        if (_started)
        {
            _worker.Join(TimeSpan.FromSeconds(2));
            _edgeWorker.Join(TimeSpan.FromSeconds(2));
        }

        _cancellation.Dispose();
    }

    private void WorkerMain()
    {
        try
        {
            BrowserTabHitTester hitTester = new(IsBrowserEnabled);

            while (_mouseEvents.Reader
                .WaitToReadAsync(_cancellation.Token)
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
            // Normal application shutdown.
        }
        catch
        {
            // Fail closed if the accessibility worker cannot continue.
        }
    }

    private void ProcessSafely(
        QueuedMouseEvent queuedEvent,
        BrowserTabHitTester hitTester)
    {
        try
        {
            Process(queuedEvent, hitTester);
        }
        catch
        {
            ResetAllGestureState();
        }
    }

    private void Process(
        QueuedMouseEvent queuedEvent,
        BrowserTabHitTester hitTester)
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

        if (BrowserGesturePolicy.RequiresNativeCloseSettling(hit))
        {
            _edgeWorker.TryWrite(new DeferredEdgeCloseRequest(
                hit,
                click,
                configuration,
                queuedEvent.InteractionGeneration));
            return;
        }

        TryFinalizeAndInject(
            hit,
            click,
            configuration,
            queuedEvent.InteractionGeneration,
            hitTester);
    }

    private static void JoinIfStarted(Thread thread)
    {
        if ((thread.ThreadState & ThreadState.Unstarted) == 0)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private Action<DeferredEdgeCloseRequest> CreateEdgeRequestProcessor()
    {
        BrowserTabHitTester hitTester = new(IsBrowserEnabled);
        return request => TryFinalizeAndInject(
            request.Target,
            request.Click,
            request.Configuration,
            request.InteractionGeneration,
            hitTester);
    }

    private void TryFinalizeAndInject(
        TabTarget hit,
        MouseClick click,
        DoubleClickConfiguration configuration,
        long interactionGeneration,
        BrowserTabHitTester hitTester)
    {
        DoubleClickConfiguration injectionConfiguration =
            BrowserGesturePolicy.GetInjectionConfiguration(hit, configuration);

        long processingAge = Environment.TickCount64 -
            click.UpMonotonicTimestampMilliseconds;
        if (!BrowserGesturePolicy.IsPostReleaseAgeAllowed(
                processingAge,
                injectionConfiguration) ||
            !IsInteractionCurrent(
                interactionGeneration,
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
                interactionGeneration,
                click.InputSequence,
                click.PointerRevision))
        {
            return;
        }

        MiddleClickInjector.TryClick(
            currentHit,
            currentPoint,
            click.UpMonotonicTimestampMilliseconds,
            injectionConfiguration,
            () => IsInteractionCurrent(
                interactionGeneration,
                click.InputSequence,
                click.PointerRevision));
    }

    private void QueueMouseEvent(MouseButtonEvent mouseEvent)
    {
        long interactionGeneration = Interlocked.Read(ref _interactionGeneration);
        if (!_mouseEvents.Writer.TryWrite(new QueuedMouseEvent(
                mouseEvent,
                interactionGeneration)))
        {
            if (Interlocked.Exchange(ref _overflowed, 1) == 0)
            {
                InvalidateInteraction();
            }
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

    private bool IsTargetEnabled(TabTarget target)
    {
        lock (_stateGate)
        {
            return _enabled &&
                   BrowserGesturePolicy.IsTargetEnabled(target, _edgeEnabled);
        }
    }

    private bool IsBrowserEnabled(BrowserKind browser)
    {
        lock (_stateGate)
        {
            return _enabled &&
                   BrowserGesturePolicy.IsBrowserEnabled(browser, _edgeEnabled);
        }
    }

    private bool IsEventCurrent(long expectedInteractionGeneration)
    {
        if (Volatile.Read(ref _overflowed) != 0 ||
            Interlocked.Read(ref _interactionGeneration) != expectedInteractionGeneration ||
            !ReadEnabled())
        {
            return false;
        }

        return Volatile.Read(ref _overflowed) == 0 &&
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
    }

    public void SetEdgeEnabled(bool enabled)
    {
        lock (_stateGate)
        {
            _edgeEnabled = enabled;
            Interlocked.Increment(ref _interactionGeneration);
            _detector.Reset();
        }

        Interlocked.Exchange(ref _resetAssemblerRequested, 1);
    }

    private void CaptureDownTarget(
        QueuedMouseEvent queuedEvent,
        BrowserTabHitTester hitTester)
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
            !IsTargetEnabled(target) ||
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
        BrowserTabHitTester hitTester,
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

    private readonly record struct DeferredEdgeCloseRequest(
        TabTarget Target,
        MouseClick Click,
        DoubleClickConfiguration Configuration,
        long InteractionGeneration);

    private readonly record struct QueuedMouseEvent(
        MouseButtonEvent MouseEvent,
        long InteractionGeneration);
}
