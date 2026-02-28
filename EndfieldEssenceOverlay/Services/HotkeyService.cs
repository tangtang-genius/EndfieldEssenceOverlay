// src/EndfieldEssenceOverlay/Services/HotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EndfieldEssenceOverlay.Services;

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _callbacks = [];
    private int _nextId = 9000;

    public void Initialize(Window window)
    {
        _hwnd   = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    public void Register(uint modifiers, uint vk, Action callback)
    {
        int id = _nextId++;
        RegisterHotKey(_hwnd, id, modifiers, vk);
        _callbacks[id] = callback;
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys)
            UnregisterHotKey(_hwnd, id);
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                           IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY &&
            _callbacks.TryGetValue(wParam.ToInt32(), out var cb))
        {
            cb();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
