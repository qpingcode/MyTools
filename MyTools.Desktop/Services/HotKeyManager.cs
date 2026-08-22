using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MyTools.Desktop.Models;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Services
{
    public class HotKeyManager
    {
        private int _searchHotKeyId = -1;
        private int _copyAndSearchHotKeyId = -1;
        private int _clipboardHotKeyId = -1;
        private int _clipboardSequentialPasteHotKeyId = -1;
        private readonly ILogger<HotKeyManager> _logger;
        private readonly HotKeyMessageHandler _hotKeyMessageHandler;

        public HotKeyManager(ILogger<HotKeyManager> logger, HotKeyMessageHandler hotKeyMessageHandler)
        {
            _logger = logger;
            _hotKeyMessageHandler = hotKeyMessageHandler;
        }

        public void RegisterySearchHotKey(HotKeyConfig? hotKey)
        {
            if (_searchHotKeyId != -1)
            {
                UnregisterHotKey(_searchHotKeyId);
                _searchHotKeyId = -1;
            }
            
            if (_copyAndSearchHotKeyId != -1)
            {
                UnregisterHotKey(_copyAndSearchHotKeyId);
                _copyAndSearchHotKeyId = -1;
            }

            if (hotKey == null || hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
            {
                _logger.LogInformation("Search hotkey is disabled.");
                return;
            }

            try
            {
                _searchHotKeyId = RegisterHotKey(hotKey.Key, hotKey.Modifiers, () => WindowHelper.ShowSearchWindow());
            }catch (InvalidOperationException ex)
            {
                MessageBox.Show("Cannot register hotkeys: " + hotKey, "HotKey Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                _logger.LogError(ex, "Cannot register hotkeys: {hotKey}", hotKey);
            }
            
        }
        
        public int RegisterHotKey(Key key, ModifierKeys modifiers, Action callback)
        {
            return _hotKeyMessageHandler.Register(key, modifiers, callback);
        }

        public void RegisterClipboardHotKey(HotKeyConfig? hotKey, Action callback)
        {
            if (_clipboardHotKeyId != -1)
            {
                UnregisterHotKey(_clipboardHotKeyId);
                _clipboardHotKeyId = -1;
            }

            if (hotKey == null || hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
            {
                _logger.LogInformation("Clipboard hotkey is disabled.");
                return;
            }

            try
            {
                _clipboardHotKeyId = RegisterHotKey(hotKey.Key, hotKey.Modifiers, callback);
                _logger.LogInformation("Registered clipboard hotkey {HotKey}.", hotKey);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Cannot register clipboard hotkey {HotKey}.", hotKey);
            }
        }

        public void RegisterClipboardSequentialPasteHotKey(HotKeyConfig? hotKey, Action callback)
        {
            if (_clipboardSequentialPasteHotKeyId != -1)
            {
                UnregisterHotKey(_clipboardSequentialPasteHotKeyId);
                _clipboardSequentialPasteHotKeyId = -1;
            }

            if (hotKey == null || hotKey.Key == Key.None || hotKey.Modifiers == ModifierKeys.None)
            {
                _logger.LogInformation("Clipboard sequential paste hotkey is disabled.");
                return;
            }

            try
            {
                _clipboardSequentialPasteHotKeyId = RegisterHotKey(hotKey.Key, hotKey.Modifiers, callback);
                _logger.LogInformation("Registered clipboard sequential paste hotkey {HotKey}.", hotKey);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Cannot register clipboard sequential paste hotkey {HotKey}.", hotKey);
            }
        }

      
        public void UnregisterHotKey(int id)
        {
            _hotKeyMessageHandler.UnregisterCallback(id);
        }

        public void UnregisterAllHotKeys()
        {
            _hotKeyMessageHandler.UnregisterAllCallback();
        }

        public void SuspendAllHotKeys()
        {
            _hotKeyMessageHandler.SuspendAll();
        }

        public void ResumeAllHotKeys()
        {
            _hotKeyMessageHandler.ResumeAll();
        }

        public bool AreHotKeysSuspended => _hotKeyMessageHandler.IsSuspended;
    }
}
