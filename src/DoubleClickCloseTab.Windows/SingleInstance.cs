namespace DoubleClickCloseTab.Windows;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    public SingleInstance(string name)
    {
        _mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsPrimary => _ownsMutex;

    public void Dispose()
    {
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
