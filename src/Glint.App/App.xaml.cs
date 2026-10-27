using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Glint.Core.Capture;
using Glint.Core.Providers;
using Glint.Core.Security;
using Glint.Providers.Gemini;
using Glint.Windows.Capture;
using Glint.Windows.Hotkeys;
using Glint.Windows.Security;

namespace Glint.App;

public partial class App : Application
{
    private WindowsCredentialStore? _credentialStore;
    private GeminiProvider? _geminiProvider;
    private IScreenCaptureService? _captureService;
    private MainWindow? _mainWindow;

    private HwndSource? _messageHwndSource;
    private GlobalHotkeyManager? _hotkeyManager;
    private string _activeHotkeyName = HotkeyConfig.DisplayName;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _credentialStore = new WindowsCredentialStore();
        _geminiProvider = new GeminiProvider(_credentialStore);
        _captureService = new ScreenCaptureService();

        _mainWindow = new MainWindow(_credentialStore, _geminiProvider);

        InitializeHotkeyListener();

        // If no API key is yet configured, show the prompt window initially so user can configure it.
        var apiKey = _credentialStore.GetApiKey(GeminiProvider.ProviderKey);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _mainWindow.Show();
            _mainWindow.Activate();
        }
    }

    private void InitializeHotkeyListener()
    {
        var parameters = new HwndSourceParameters("GlintHotkeyListenerWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = -1000,
            PositionY = -1000,
            WindowStyle = 0x800000, // WS_BORDER
            ExtendedWindowStyle = 0x80 // WS_EX_TOOLWINDOW
        };

        _messageHwndSource = new HwndSource(parameters);
        _messageHwndSource.AddHook(HwndHook);

        _hotkeyManager = new GlobalHotkeyManager(_messageHwndSource.Handle);

        var (success, _) = _hotkeyManager.TryRegister(
            HotkeyConfig.Modifiers,
            HotkeyConfig.VirtualKey,
            OnHotkeyTriggered);

        if (success)
        {
            _activeHotkeyName = HotkeyConfig.DisplayName;
            _mainWindow?.UpdateHotkeyBadge(_activeHotkeyName);
        }
        else
        {
            _activeHotkeyName = $"{HotkeyConfig.DisplayName} (Failed)";
            _mainWindow?.UpdateHotkeyBadge(_activeHotkeyName);

            MessageBox.Show(
                $"Failed to register global hotkey '{HotkeyConfig.DisplayName}'.\n\nThe shortcut is already taken by another application or Windows.",
                "Glint - Hotkey Registration Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeyManager != null && _hotkeyManager.TryProcessMessage(msg, wParam))
        {
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnHotkeyTriggered()
    {
        _ = HandleHotkeyAsync();
    }

    private async Task HandleHotkeyAsync()
    {
        if (_captureService == null || _mainWindow == null) return;

        try
        {
            // 1. Capture primary screen BEFORE showing or focusing the prompt window
            var screenshot = await _captureService.CapturePrimaryScreenPngAsync();

            // 2. Now show the topmost prompt window with the captured screenshot
            Dispatcher.Invoke(() =>
            {
                _mainWindow.SetPendingScreenshot(screenshot);
                _mainWindow.Show();
                _mainWindow.Activate();
                _mainWindow.Focus();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Failed to capture screen: {ex.Message}", "Glint Capture Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _messageHwndSource?.Dispose();
        base.OnExit(e);
    }
}
