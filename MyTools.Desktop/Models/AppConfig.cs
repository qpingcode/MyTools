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

        [JsonProperty("Theme")]
        public string Theme { get; set; } = "dark";

        [JsonProperty("EnableClipboardHistory")]
        public bool EnableClipboardHistory { get; set; } = true;
    }
}