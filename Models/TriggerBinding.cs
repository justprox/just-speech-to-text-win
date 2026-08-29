using System.Text.Json.Serialization;

namespace JustSTT.Models
{
    public enum TriggerType
    {
        KeyboardKey,
        MouseButton
    }

    public class TriggerBinding
    {
        public TriggerType Type { get; set; } = TriggerType.KeyboardKey;
        
        // Virtual Key Code (e.g. 0xA3 for Right Ctrl)
        public int KeyCode { get; set; } = 0xA3;

        // Mouse button: 1=Left, 2=Right, 3=Middle, 4=XButton1 (Mouse 4), 5=XButton2 (Mouse 5)
        public int MouseButton { get; set; } = 0;

        public string DisplayName { get; set; } = "Right Ctrl";

        public override string ToString() => DisplayName;

        public static TriggerBinding RightControl => new TriggerBinding
        {
            Type = TriggerType.KeyboardKey,
            KeyCode = 0xA3, // VK_RCONTROL
            MouseButton = 0,
            DisplayName = "Right Ctrl"
        };

        public static TriggerBinding Mouse5 => new TriggerBinding
        {
            Type = TriggerType.MouseButton,
            KeyCode = 0,
            MouseButton = 5, // XButton2
            DisplayName = "Mouse 5 (Forward)"
        };

        public static TriggerBinding Mouse4 => new TriggerBinding
        {
            Type = TriggerType.MouseButton,
            KeyCode = 0,
            MouseButton = 4, // XButton1
            DisplayName = "Mouse 4 (Back)"
        };

        public bool MatchesKey(int vkCode)
        {
            if (Type != TriggerType.KeyboardKey) return false;
            // Also handle general VK_CONTROL if VK_RCONTROL
            if (KeyCode == 0xA3 && (vkCode == 0xA3 || vkCode == 0x11)) return true;
            return KeyCode == vkCode;
        }

        public bool MatchesMouse(int button)
        {
            return Type == TriggerType.MouseButton && MouseButton == button;
        }
    }
}
