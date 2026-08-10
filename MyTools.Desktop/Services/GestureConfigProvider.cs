using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Desktop.Models;

namespace MyTools.Desktop.Services;

/// <summary>
/// 用户配置的鼠标手势列表，存储在 %AppData%/MyTools.Desktop/Gestures.json 中。
/// </summary>
public sealed class GestureConfigProvider
{
    private static readonly string FilePath = Path.Combine(ConfigPath.Base, "Gestures.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<GestureConfigProvider> logger;
    private List<GestureConfig> gestures = new();

    public GestureConfigProvider(ILogger<GestureConfigProvider> logger)
    {
        this.logger = logger;
        Load();
    }

    public List<GestureConfig> GetAll()
    {
        return gestures;
    }

    public void Save(List<GestureConfig> newGestures)
    {
        gestures = newGestures;
        Persist();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                // 首次使用：写入默认手势
                gestures = GetDefaults();
                Persist();
                return;
            }

            var json = File.ReadAllText(FilePath);
            gestures = JsonSerializer.Deserialize<List<GestureConfig>>(json, JsonOptions)
                        ?? GetDefaults();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load Gestures.json.");
            gestures = GetDefaults();
        }
    }

    private void Persist()
    {
        try
        {
            Directory.CreateDirectory(ConfigPath.Base);
            var json = JsonSerializer.Serialize(gestures, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save Gestures.json.");
        }
    }

    /// <summary>
    /// 返回原有硬编码的默认手势集合。
    /// </summary>
    public static List<GestureConfig> GetDefaults()
    {
        return
        [
            new GestureConfig
            {
                Directions = ["Down", "Right"],
                ActionName = "Close Tab",
                ActionType = "hotkey",
                HotKey = "Control+W",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = ["Down", "Right"],
                ActionName = "Close Tab",
                ActionType = "hotkey",
                HotKey = "Control+F4",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
            new GestureConfig
            {
                Directions = ["Up", "Right"],
                ActionName = "Create New",
                ActionType = "hotkey",
                HotKey = "Control+N",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = ["Up", "Right"],
                ActionName = "Create New Tab",
                ActionType = "hotkey",
                HotKey = "Control+T",
                ProcessNames = ["chrome", "firefox", "edge"]
            },
            new GestureConfig
            {
                Directions = ["Left"],
                ActionName = "Back",
                ActionType = "mouse",
                MouseButton = "XButton1",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = ["Right"],
                ActionName = "Forward",
                ActionType = "mouse",
                MouseButton = "XButton2",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = ["Up", "Down"],
                ActionName = "Refresh Page",
                ActionType = "hotkey",
                HotKey = "F5",
                ProcessNames = ["chrome", "firefox", "edge"]
            },
            new GestureConfig
            {
                Directions = ["Down", "Right", "Up", "Left"],
                ActionName = "Close Other Tabs",
                ActionType = "hotkey",
                HotKey = "Alt+Shift+O",
                ProcessNames = []
            },
            new GestureConfig
            {
                Directions = ["Left", "Right"],
                ActionName = "Full Screen Switch",
                ActionType = "hotkey",
                HotKey = "Control+Shift+F12",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
            new GestureConfig
            {
                Directions = ["Right", "Left"],
                ActionName = "Full Screen Switch",
                ActionType = "hotkey",
                HotKey = "Control+Shift+F12",
                ProcessNames = ["rider", "rider64", "devenv"]
            },
        ];
    }
}
