using System.IO;
using Newtonsoft.Json;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;

namespace MyTools.Desktop.Storage;

/// <summary>
/// JSON配置存储实现
/// </summary>
public class JsonConfigurationStorage : IConfigurationStorage, IDisposable
{
    private readonly string _jsonFilePath;
    private Dictionary<string, StoredSetting> _settings;
    private readonly object _lockObject = new object();
    private bool _disposed ;
    
    public JsonConfigurationStorage()
        : this(Path.Combine(ConfigPath.Base, "Settings.json"))
    {
    }

    public JsonConfigurationStorage(string jsonFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonFilePath);
        _jsonFilePath = jsonFilePath;
        _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
        Initialize();
    }
    
    /// <summary>
    /// 初始化存储
    /// </summary>
    public void Initialize()
    {
        try
        {
            LoadSettings();
        }
        catch (Exception)
        {
            // 如果加载失败，使用空的设置集合
            _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
        }
    }
    
    /// <summary>
    /// 存储配置值
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <param name="value">配置值</param>
    public void Store(string name, string value, PluginId? pluginId = null)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("配置项名称不能为空", nameof(name));
        
        lock (_lockObject)
        {
            var setting = new StoredSetting
            {
                Name = name,
                Value = value,
                LastModified = DateTime.UtcNow
            };
            
            _settings[name] = setting;
            SaveSettings();
        }
    }
    
    /// <summary>
    /// 获取配置值
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>配置值，如果不存在返回null</returns>
    public string? Retrieve(string name, PluginId? pluginId = null)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        
        lock (_lockObject)
        {
            return _settings.TryGetValue(name, out var setting) ? setting.Value : null;
        }
    }
    
    /// <summary>
    /// 检查配置项是否存在
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>是否存在</returns>
    public bool Exists(string name, PluginId? pluginId = null)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        
        lock (_lockObject)
        {
            return _settings.ContainsKey(name);
        }
    }
    
    /// <summary>
    /// 删除配置项
    /// </summary>
    /// <param name="name">配置项名称</param>
    public void Delete(string name, PluginId? pluginId = null)
    {
        if (string.IsNullOrEmpty(name))
            return;
        
        lock (_lockObject)
        {
            if (_settings.Remove(name))
            {
                SaveSettings();
            }
        }
    }
    
    /// <summary>
    /// 清空所有配置
    /// </summary>
    public void Clear()
    {
        lock (_lockObject)
        {
            _settings.Clear();
            SaveSettings();
        }
    }
    
    /// <summary>
    /// 获取所有配置项名称
    /// </summary>
    /// <returns>配置项名称集合</returns>
    public IEnumerable<string> GetAllNames(PluginId? pluginId = null)
    {
        lock (_lockObject)
        {
            return _settings.Keys.ToList();
        }
    }
    
    /// <summary>
    /// 加载设置
    /// </summary>
    private void LoadSettings()
    {
        if (!File.Exists(_jsonFilePath))
        {
            _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
            return;
        }
        
        try
        {
            var jsonContent = File.ReadAllText(_jsonFilePath);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
                return;
            }
            
            var settingsList = JsonConvert.DeserializeObject<List<StoredSetting>>(jsonContent);
            if (settingsList != null)
            {
                _settings = settingsList.ToDictionary(
                    setting => setting.Name,
                    setting => setting,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            // 如果反序列化失败，使用空的设置集合
            _settings = new Dictionary<string, StoredSetting>(StringComparer.OrdinalIgnoreCase);
        }
    }
    
    /// <summary>
    /// 保存设置
    /// </summary>
    private void SaveSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_jsonFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var settingsList = _settings.Values.ToList();
            var jsonContent = JsonConvert.SerializeObject(settingsList, Formatting.Indented);
            File.WriteAllText(_jsonFilePath, jsonContent);
        }
        catch (Exception ex)
        {
            // 记录错误但不抛出异常，避免影响应用程序运行
            System.Diagnostics.Debug.WriteLine($"保存配置到JSON文件失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// 释放资源
    /// </summary>
    /// <param name="disposing">是否正在释放托管资源</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                SaveSettings();
            }
            catch
            {
                // 忽略保存时的错误
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// 存储的设置项
/// </summary>
public class StoredSetting
{
    /// <summary>
    /// 配置项名称
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 配置值
    /// </summary>
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;
    
    /// <summary>
    /// 最后修改时间
    /// </summary>
    [JsonProperty("lastModified")]
    public DateTime LastModified { get; set; }
}
