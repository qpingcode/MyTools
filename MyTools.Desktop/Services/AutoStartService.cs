using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace MyTools.Desktop.Services;

public class AutoStartService
{
    private const string AppName = "MyTools.Desktop";
    private const string AutoStartRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private bool autoStart;
    private readonly ILogger<AutoStartService> logger;

    public AutoStartService(ILogger<AutoStartService> logger)
    {
        this.logger = logger;
        CheckAutoStartStatus();
        RepairAutoStartPathIfEnabled();
    }
    
    public bool AutoStart
    {
        get => autoStart;
        set
        {
            SetAutoStart(value);
        }
    }
    
    private void SetAutoStart(bool enable)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryKey, true))
            {
                if (enable)
                {
                    var command = GetAutoStartCommand();
                    logger.LogDebug("Set auto startup: {Command}", command);
                    key.SetValue(AppName, command);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }

            autoStart = enable;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置开机启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            logger.LogError(ex, "Failed to set auto startup to {Enabled}.", enable);
        }
    }
    
    private void CheckAutoStartStatus()
    {
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false))
        {
            autoStart = key?.GetValue(AppName) != null;
        }
    }

    private void RepairAutoStartPathIfEnabled()
    {
        if (!autoStart)
        {
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryKey, true);
            var expectedCommand = GetAutoStartCommand();
            var currentCommand = key.GetValue(AppName) as string;
            if (!string.Equals(currentCommand, expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Repairing auto startup path after an application move or update.");
                key.SetValue(AppName, expectedCommand);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to repair the auto startup path.");
        }
    }

    private static string GetAutoStartCommand()
    {
        var appPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(appPath))
        {
            appPath = Assembly.GetExecutingAssembly().Location;
            if (appPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                appPath = Path.ChangeExtension(appPath, ".exe");
            }
        }

        return $"\"{appPath}\"";
    }
}