using System;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Glint.Windows.Hotkeys;

public sealed class HotkeyRegistration : IDisposable
{
    private readonly HWND _hWnd;
    private readonly int _id;
    private bool _disposed;

    public int Id => _id;

    public HotkeyRegistration(IntPtr hWnd, int id, KeyModifiers modifiers, uint vk)
    {
        _hWnd = (HWND)hWnd;
        _id = id;
        if (!PInvoke.RegisterHotKey(_hWnd, _id, (HOT_KEY_MODIFIERS)(uint)modifiers, vk))
        {
            throw new InvalidOperationException($"Failed to register hotkey id {_id} (VK: 0x{vk:X}, Modifiers: {modifiers}).");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            PInvoke.UnregisterHotKey(_hWnd, _id);
            _disposed = true;
        }
    }
}
