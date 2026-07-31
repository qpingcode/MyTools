using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Desktop.Models;
using Newtonsoft.Json;

namespace MyTools.Desktop
{
    public class AppConfig : IAppConfig
    {
        [JsonProperty("SearchHotKey")]
        public string SearchHotKeyText { get; set; } = "Alt+Space";

        [JsonIgnore]
        public HotKeyConfig SearchHotKey => new(SearchHotKeyText);

        [JsonProperty("Language")]
        public string Language { get; set; } = "en-US";

        [JsonProperty("EnableGesture")]
        public bool EnableGesture { get; set; } = false;

        [JsonProperty("EnableClipboardHistory")]
        public bool EnableClipboardHistory { get; set; } = true;
    }
}