using System.IO;
using System.Windows;
using MyTools.Common.Config;
using MyTools.Desktop.Models;
using Newtonsoft.Json;

namespace MyTools.Desktop.Services;

public class AppConfigService
{
    private static readonly string ConfigFilePath = Path.Combine(
        ConfigPath.Base,
        "MyToolsConfig.json");

    private IAppConfig? appConfig;
    public IAppConfig AppConfig
    {
        get
        {
            if (appConfig == null)
            {
                appConfig = LoadConfig();
            }
            return appConfig;
        }    
    }
    private IAppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFilePath))
            {
                string json = File.ReadAllText(ConfigFilePath);
                var loadedConfig = JsonConvert.DeserializeObject<AppConfig>(json);
                if (loadedConfig != null)
                {
                    return loadedConfig;
                }
            }
            else
            {
                return EnsureAppConfigCreatedWhenMissing();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载配置文件时出错: {ex.Message}", "配置错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        
        return new AppConfig();
    }

    
    private IAppConfig EnsureAppConfigCreatedWhenMissing()
    {
        string directory = Path.GetDirectoryName(ConfigFilePath) ?? throw new ArgumentNullException(ConfigFilePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var config = new AppConfig();
        SaveConfig(config);

        return config;
    }

    private void SaveConfig(IAppConfig config)
    {
        try
        {
            string json = JsonConvert.SerializeObject(config);
            File.WriteAllText(ConfigFilePath, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save config file error: {ex.Message}", "Config Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void SetLanguage(string cultureName)
    {
        AppConfig.Language = cultureName;
        SaveConfig(AppConfig);
    }

    public void SetTheme(string theme)
    {
        AppConfig.Theme = theme;
        SaveConfig(AppConfig);
    }
}