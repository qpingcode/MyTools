using System.Windows.Input;

namespace MyTools.Desktop.Models
{
    public class HotKeyConfig
    {
        public HotKeyConfig(Key key, ModifierKeys modifiers)
        {
            Key = key;
            Modifiers = modifiers;
        }
        
        public HotKeyConfig(string? hotKeyText)
        {
            Key = GetKeyFromText(hotKeyText);
            Modifiers = GetModifiersFromText(hotKeyText);
        }
        
        public Key Key { get; }

        public ModifierKeys Modifiers { get; }

        private Key GetKeyFromText(string? hotKeyText)
        {
            if (string.IsNullOrEmpty(hotKeyText))
            {
                return Key.None;
            }

            string[] parts = hotKeyText.Split('+');
            if (parts.Length == 0)
            {
                return Key.None;
            }

            string keyPart = parts[parts.Length - 1];
            if (Enum.TryParse(keyPart, out Key key))
            {
                return key;
            }

            return Key.None;
        }
        private ModifierKeys GetModifiersFromText(string? hotKeyText)
        {
            if (string.IsNullOrEmpty(hotKeyText))
            {
                return ModifierKeys.None;
            }

            string[] parts = hotKeyText.Split('+');
            ModifierKeys modifiers = ModifierKeys.None;

            foreach (string part in parts)
            {
                var token = part.Trim();
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)
                         || token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                else if (Enum.TryParse(token, ignoreCase: true, out ModifierKeys modifier))
                {
                    modifiers |= modifier;
                }
            }

            return modifiers;
        }
        
        public override string? ToString()
        {
            string modifierText = "";

            if ((Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                modifierText += "Ctrl+";

            if ((Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                modifierText += "Shift+";

            if ((Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                modifierText += "Alt+";

            if ((Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                modifierText += "Win+";

            return $"{modifierText}{Key}";
        }
    }
}