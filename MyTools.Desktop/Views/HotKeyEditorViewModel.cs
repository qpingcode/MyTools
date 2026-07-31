using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Desktop.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTools.Desktop.Views
{
    public partial class HotKeyEditorViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HotKeyText))]
        private Key key;
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HotKeyText))]
        private ModifierKeys modifiers;

        public string HotKeyText => new HotKeyConfig(Key, Modifiers).ToString() ?? string.Empty;
        
        public HotKeyConfig HotKey => new(Key, Modifiers);

        public HotKeyEditorViewModel(HotKeyConfig currentHotKey)
        {
            Key = currentHotKey.Key;
            Modifiers = currentHotKey.Modifiers;
        }

        public void HandleKeyDown(KeyEventArgs e)
        {
            Key eventKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (eventKey == Key.System || eventKey == Key.Tab || eventKey == Key.Escape)
            {
                return;
            }

            Modifiers = Keyboard.Modifiers;
            Key = IsModifierKey(eventKey) ? Key.None : eventKey;
            e.Handled = true;
        }
        
        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.LeftAlt || key == Key.RightAlt;
        }

        public bool IsValidHotKey()
        {
            return Key != Key.None && Modifiers != ModifierKeys.None;
        }
    }
}