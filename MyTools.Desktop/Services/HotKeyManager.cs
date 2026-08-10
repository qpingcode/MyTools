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
        private readonly ILogger<HotKeyManager> _logger;
        private readonly HotKeyMessageHandler _hotKeyMessageHandler;

        public HotKeyManager(ILogger<HotKeyManager> logger, HotKeyMessageHandler hotKeyMessageHandler)
        {
            _logger = logger;
            _hotKeyMessageHandler = hotKeyMessageHandler;
        }

        public void RegisterySearchHotKey(HotKeyConfig hotKey)
        {
            if (_searchHotKeyId != -1)
            {
                UnregisterHotKey(_searchHotKeyId);
            }
            
            if (_copyAndSearchHotKeyId != -1)
            {
                UnregisterHotKey(_copyAndSearchHotKeyId);
            }

            if (hotKey == null)
            {
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
    }
}