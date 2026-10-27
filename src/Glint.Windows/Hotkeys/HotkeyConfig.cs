namespace Glint.Windows.Hotkeys;

public static class HotkeyConfig
{
    public const string DisplayName = "Alt+Space";
    public const KeyModifiers Modifiers = KeyModifiers.Alt | KeyModifiers.NoRepeat;
    public const uint VirtualKey = 0x20; // VK_SPACE
}
