namespace TabCloser.Windows;

internal sealed class SingleInstance : IDisposable
{
    private const string RestoreEventSuffix = ".RestoreTrayIcon";

    private readonly EventWaitHandle _restoreTrayIconEvent;
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public SingleInstance(string name)
    {
        _restoreTrayIconEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            name + RestoreEventSuffix);

        try
        {
            _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
            _ownsMutex = createdNew;
        }
        catch
        {
            _restoreTrayIconEvent.Dispose();
            throw;
        }
    }

    public bool IsPrimary => _ownsMutex;

    public void RequestTrayIconRestore() => _restoreTrayIconEvent.Set();

    public bool ConsumeTrayIconRestoreRequest() =>
        _restoreTrayIconEvent.WaitOne(0);

    public void Dispose()
    {
        _restoreTrayIconEvent.Dispose();

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
