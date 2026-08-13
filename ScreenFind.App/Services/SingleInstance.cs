namespace ScreenFind.App.Services;

/// <summary>
/// Only one ScreenFind may run at a time: a second copy would fail to register the global hotkey
/// and leave the user with an app that appears to do nothing. A second launch signals the running
/// instance to open the search bar and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ScreenFind.SingleInstance";
    private const string SignalName = @"Local\ScreenFind.ShowRequested";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly CancellationTokenSource _cancellation = new();

    private SingleInstance(Mutex mutex, EventWaitHandle signal)
    {
        _mutex = mutex;
        _signal = signal;

        var thread = new Thread(WaitLoop) { IsBackground = true, Name = "ScreenFind.SingleInstance" };
        thread.Start();
    }

    /// <summary>Raised when another launch asked this instance to surface.</summary>
    public event Action? ShowRequested;

    /// <summary>Returns null when another instance already owns the name.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        return new SingleInstance(mutex, signal);
    }

    /// <summary>Asks the already running instance to show itself.</summary>
    public static bool SignalExisting()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(SignalName, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void WaitLoop()
    {
        var handles = new WaitHandle[] { _signal, _cancellation.Token.WaitHandle };
        while (!_cancellation.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny(handles) == 0) ShowRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        _mutex.Dispose();
        _signal.Dispose();
        _cancellation.Dispose();
    }
}
