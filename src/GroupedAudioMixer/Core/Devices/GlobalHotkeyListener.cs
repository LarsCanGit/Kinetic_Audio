using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace GroupedAudioMixer.Core.Devices;

/// <summary>
/// Win32 global hotkey registration (<c>RegisterHotKey</c>/<c>WM_HOTKEY</c>) via a message-only
/// window. Per binding decision D5 this is a thin OS-interop adapter with no logic of its own
/// (dispatch by id only); it is not unit-tested and is covered by the D6 manual/E2E regression
/// pass for the device-switch hotkey, same as <see cref="CoreAudioDeviceProvider"/>.
/// </summary>
public sealed class GlobalHotkeyListener : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _hwndSource;
    private readonly Dictionary<int, Action> _handlersById = new();
    private int _nextId;

    public GlobalHotkeyListener()
    {
        var parameters = new HwndSourceParameters("GroupedAudioMixerHotkeyWindow")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE: message-only window, no UI surface
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);
    }

    /// <summary>
    /// Registers a global hotkey and returns a disposable registration; disposing it unregisters
    /// the hotkey. <paramref name="callback"/> runs on the WPF dispatcher thread.
    /// </summary>
    public IDisposable Register(ModifierKeys modifiers, Key key, Action callback)
    {
        var id = _nextId++;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (!RegisterHotKey(_hwndSource.Handle, id, (uint)modifiers, vk))
            throw new InvalidOperationException($"Failed to register hotkey (modifiers={modifiers}, key={key}). It may already be in use by another application.");

        _handlersById[id] = callback;
        return new Registration(this, id);
    }

    private void Unregister(int id)
    {
        UnregisterHotKey(_hwndSource.Handle, id);
        _handlersById.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlersById.TryGetValue(wParam.ToInt32(), out var callback))
        {
            callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlersById.Keys.ToList())
            Unregister(id);

        _hwndSource.RemoveHook(WndProc);
        _hwndSource.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        private readonly GlobalHotkeyListener _owner;
        private readonly int _id;
        private bool _disposed;

        public Registration(GlobalHotkeyListener owner, int id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _owner.Unregister(_id);
        }
    }
}
