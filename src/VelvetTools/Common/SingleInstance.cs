namespace VelvetTools.Common;

/// <summary>单实例保障：第二个实例启动时通知第一个实例弹出面板后退出。</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = "VelvetTools.SingleInstance.Mutex";
    private const string EventName = "VelvetTools.SingleInstance.ShowEvent";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Thread? _listener;
    private volatile bool _disposed;

    /// <summary>尝试成为主实例；若已有实例在运行则发出唤起信号并返回 false。</summary>
    public bool TryAcquire(Action onActivateRequested)
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var evt = EventWaitHandle.OpenExisting(EventName);
                evt.Set();
            }
            catch { }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _listener = new Thread(() =>
        {
            while (!_disposed)
            {
                try
                {
                    if (_showEvent.WaitOne(1000))
                        onActivateRequested();
                }
                catch { return; }
            }
        })
        { IsBackground = true, Name = "VelvetTools.SingleInstance" };
        _listener.Start();
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}
