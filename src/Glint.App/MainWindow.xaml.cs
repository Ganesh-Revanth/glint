using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Glint.Core.Providers;
using Glint.Core.Security;

namespace Glint.App;

public partial class MainWindow : Window
{
    private readonly ICredentialStore _credentialStore;
    private readonly IModelProvider _modelProvider;
    private byte[]? _currentScreenshotPng;
    private bool _isBusy;

    public MainWindow(ICredentialStore credentialStore, IModelProvider modelProvider)
    {
        InitializeComponent();
        _credentialStore = credentialStore;
        _modelProvider = modelProvider;

        CheckApiKeyStatus();
    }

    public void SetPendingScreenshot(byte[] screenshotPng)
    {
        _currentScreenshotPng = screenshotPng;
        AnswerPanel.Visibility = Visibility.Collapsed;
        AnswerTextBox.Clear();
        PromptTextBox.Clear();
        StatusPanel.Visibility = Visibility.Collapsed;
        PromptTextBox.Focus();
    }

    public void UpdateHotkeyBadge(string hotkeyText)
    {
        HotkeyBadge.Text = hotkeyText;
    }

    private void CheckApiKeyStatus()
    {
        var existingKey = _credentialStore.GetApiKey(GeminiProviderKey());
        if (string.IsNullOrWhiteSpace(existingKey))
        {
            ApiKeyPanel.Visibility = Visibility.Visible;
            ApiKeyStatusText.Text = "Please configure your Gemini API key to start.";
            ApiKeyStatusText.Foreground = System.Windows.Media.Brushes.Coral;
        }
        else
        {
            ApiKeyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string GeminiProviderKey() => "Gemini";

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void BtnToggleKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyPanel.Visibility = ApiKeyPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (ApiKeyPanel.Visibility == Visibility.Visible)
        {
            ApiKeyBox.Focus();
        }
    }

    private void BtnSaveKey_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key))
        {
            ApiKeyStatusText.Text = "API key cannot be empty.";
            ApiKeyStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            return;
        }

        try
        {
            _credentialStore.SaveApiKey(GeminiProviderKey(), key.Trim());
            ApiKeyBox.Clear();
            ApiKeyStatusText.Text = "Key securely saved to Windows Credential Manager!";
            ApiKeyStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

            Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    ApiKeyPanel.Visibility = Visibility.Collapsed;
                });
            });
        }
        catch (Exception ex)
        {
            ApiKeyStatusText.Text = $"Error saving key: {ex.Message}";
            ApiKeyStatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
        }
    }

    private void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = SendPromptAsync();
        }
    }

    private void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        _ = SendPromptAsync();
    }

    private async Task SendPromptAsync()
    {
        if (_isBusy) return;

        var key = _credentialStore.GetApiKey(GeminiProviderKey());
        if (string.IsNullOrWhiteSpace(key))
        {
            ApiKeyPanel.Visibility = Visibility.Visible;
            ApiKeyBox.Focus();
            ApiKeyStatusText.Text = "A Gemini API key is required. Please enter it above.";
            ApiKeyStatusText.Foreground = System.Windows.Media.Brushes.Coral;
            return;
        }

        if (_currentScreenshotPng == null || _currentScreenshotPng.Length == 0)
        {
            AnswerPanel.Visibility = Visibility.Visible;
            AnswerTextBox.Text = "No screenshot captured. Press the hotkey again to capture your screen.";
            return;
        }

        var prompt = PromptTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            prompt = "What is on my screen right now?";
        }

        try
        {
            _isBusy = true;
            BtnSend.IsEnabled = false;
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Analyzing screen with Gemini...";
            AnswerPanel.Visibility = Visibility.Collapsed;

            var reply = await _modelProvider.GenerateReplyAsync(prompt, _currentScreenshotPng);

            AnswerPanel.Visibility = Visibility.Visible;
            AnswerTextBox.Text = reply;
        }
        catch (Exception ex)
        {
            AnswerPanel.Visibility = Visibility.Visible;
            AnswerTextBox.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            BtnSend.IsEnabled = true;
            StatusPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(AnswerTextBox.Text))
        {
            Clipboard.SetText(AnswerTextBox.Text);
            BtnCopy.Content = "Copied!";
            Task.Delay(1500).ContinueWith(_ =>
            {
                Dispatcher.Invoke(() => BtnCopy.Content = "Copy");
            });
        }
    }
}