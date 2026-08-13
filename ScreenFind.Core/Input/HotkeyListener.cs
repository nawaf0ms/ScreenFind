using ScreenFind.Core.Interop;

namespace ScreenFind.Core.Input;

public static class VirtualKeys
{
    public const uint F = 0x46;
    public const uint F3 = 0x72;
    public const uint Escape = 0x1B;
    public const uint Enter = 0x0D;
}

/// <param name="Modifiers">Combination of Win32.MOD_* flags.</param>
public sealed record HotkeyDefinition(uint Modifiers, uint VirtualKey, string Display)
{
    /// <summary>Ctrl+Shift+F — the default trigger (spec §4).</summary>
    public static readonly HotkeyDefinition Default =
        new(Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, VirtualKeys.F, "Ctrl+Shift+F");
}

/// <summary>
/// Global hotkey on a dedicated thread. Registering with a null window handle posts WM_HOTKEY to
/// the calling thread's message queue, so no window class or WPF dependency is needed — which
/// keeps this usable from both the console harness and the WPF app.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private readonly HotkeyDefinition _definition;
    private readonly int _id;
    private Thread? _thread;
    private uint _threadId;
    private bool _disposed;

    public HotkeyListener(HotkeyDefinition? definition = null, int id = 0xB1F)
    {
        _definition = definition ?? HotkeyDefinition.Default;
        _id = id;
    }

    /// <summary>Raised on the listener thread — marshal to the UI thread before touching the UI.</summary>
    public event Action? Pressed;

    /// <summary>Raised when registration fails, usually because another app owns the combination.</summary>
    public event Action<string>? Failed;

    public HotkeyDefinition Definition => _definition;

    public void Start()
    {
        if (_thread is not null) return;

        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "ScreenFind.Hotkey"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = Win32.GetCurrentThreadId();

        if (!Win32.RegisterHotKey(IntPtr.Zero, _id, _definition.Modifiers, _definition.VirtualKey))
        {
            ready.Set();
            Failed?.Invoke($"Could not register {_definition.Display} — another application probably owns it.");
            return;
        }

        ready.Set();

        try
        {
            while (Win32.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == Win32.WM_HOTKEY && message.wParam.ToInt32() == _id)
                {
                    Pressed?.Invoke();
                }
            }
        }
        finally
        {
            Win32.UnregisterHotKey(IntPtr.Zero, _id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_threadId != 0) Win32.PostThreadMessage(_threadId, Win32.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }
}
