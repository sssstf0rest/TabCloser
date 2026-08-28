using System.Threading.Channels;

namespace TabCloser.Windows.Input;

internal sealed class DeferredRequestWorker<TRequest>
    where TRequest : notnull
{
    private readonly Channel<TRequest> _requests;
    private readonly CancellationToken _cancellationToken;
    private readonly int _delayMilliseconds;
    private readonly Func<int, CancellationToken, bool> _waitForCancellation;
    private readonly Func<Action<TRequest>> _processorFactory;
    private readonly Thread _thread;

    internal DeferredRequestWorker(
        CancellationToken cancellationToken,
        int delayMilliseconds,
        Func<Action<TRequest>> processorFactory,
        string threadName,
        Func<int, CancellationToken, bool>? waitForCancellation = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);
        _cancellationToken = cancellationToken;
        _delayMilliseconds = delayMilliseconds;
        _processorFactory = processorFactory;
        _waitForCancellation = waitForCancellation ?? WaitForCancellation;
        _requests = Channel.CreateBounded<TRequest>(
            new BoundedChannelOptions(capacity: 1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = threadName,
        };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    internal void Start()
    {
        _thread.Start();
    }

    internal bool TryWrite(TRequest request)
    {
        return _requests.Writer.TryWrite(request);
    }

    internal void Complete()
    {
        _requests.Writer.TryComplete();
    }

    internal bool Join(TimeSpan timeout)
    {
        return (_thread.ThreadState & ThreadState.Unstarted) != 0 ||
               _thread.Join(timeout);
    }

    private void ThreadMain()
    {
        try
        {
            Action<TRequest>? process = null;
            while (_requests.Reader
                .WaitToReadAsync(_cancellationToken)
                .AsTask()
                .GetAwaiter()
                .GetResult())
            {
                while (_requests.Reader.TryRead(out TRequest? request))
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    process ??= _processorFactory();
                    if (_waitForCancellation(
                            _delayMilliseconds,
                            _cancellationToken) ||
                        _cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    try
                    {
                        process(request);
                    }
                    catch
                    {
                        // One failed request must not stop later requests.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        catch
        {
            // Fail closed if the deferred worker cannot continue.
        }
    }

    private static bool WaitForCancellation(
        int delayMilliseconds,
        CancellationToken cancellationToken) =>
        cancellationToken.WaitHandle.WaitOne(delayMilliseconds);
}
