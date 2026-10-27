using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Glint.Windows.Hotkeys;

public sealed class GlobalHotkeyManager : IDisposable
{
    public const int WM_HOTKEY = 0x0312;
    private readonly IntPtr _hWnd;
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _currentId = 9000;
    private bool _disposed;

    public GlobalHotkeyManager(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    public (bool Success, int HotkeyId) TryRegister(KeyModifiers modifiers, uint vk, Action callback)
    {
        var id = ++_currentId;
        try
        {
            _ = new HotkeyRegistration(_hWnd, id, modifiers, vk);
            _callbacks[id] = callback;
            return (true, id);
        }
        catch
        {
            return (false, 0);
        }
    }

    public bool TryProcessMessage(int msg, IntPtr wParam)
    {
        if (msg == WM_HOTKEY)
        {
            var id = (int)wParam;
            if (_callbacks.TryGetValue(id, out var callback))
            {
                callback();
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var id in _callbacks.Keys)
            {
                PInvoke.UnregisterHotKey((HWND)_hWnd, id);
            }
            _callbacks.Clear();
            _disposed = true;
        }
    }
}
