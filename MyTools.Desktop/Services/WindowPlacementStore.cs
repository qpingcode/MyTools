using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;

namespace MyTools.Desktop.Services;

internal sealed class WindowPlacementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger logger;
    private readonly string filePath;
    private readonly object syncRoot = new();
    private Dictionary<string, Dictionary<string, WindowPlacementRecord>> placements =
        new(StringComparer.OrdinalIgnoreCase);

    public WindowPlacementStore(ILogger logger, string? filePath = null)
    {
        this.logger = logger;
        this.filePath = filePath ?? Path.Combine(ConfigPath.Base, "WindowPlacements.json");
        Load();
    }

    public WindowPlacementRecord? Find(string windowKey, string monitorDeviceName)
    {
        lock (syncRoot)
        {
            if (placements.TryGetValue(windowKey, out var byMonitor)
                && byMonitor.TryGetValue(monitorDeviceName, out var record))
            {
                return Clone(record);
            }

            return null;
        }
    }

    public void Save(string windowKey, string monitorDeviceName, WindowPlacementRecord record)
    {
        lock (syncRoot)
        {
            if (!placements.TryGetValue(windowKey, out var byMonitor))
            {
                byMonitor = new Dictionary<string, WindowPlacementRecord>(StringComparer.OrdinalIgnoreCase);
                placements[windowKey] = byMonitor;
            }

            byMonitor[monitorDeviceName] = Clone(record);
            Persist();
        }
    }

    private void Load()
    {
        try
        {
            placements = new Dictionary<string, Dictionary<string, WindowPlacementRecord>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(filePath))
            {
                return;
            }

            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            foreach (var window in document.RootElement.EnumerateObject())
            {
                var byMonitor = ReadWindowEntry(window.Value);
                if (byMonitor.Count > 0)
                {
                    placements[window.Name] = byMonitor;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load WindowPlacements.json.");
            placements = new Dictionary<string, Dictionary<string, WindowPlacementRecord>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, WindowPlacementRecord> ReadWindowEntry(JsonElement value)
    {
        var byMonitor = new Dictionary<string, WindowPlacementRecord>(StringComparer.OrdinalIgnoreCase);
        if (value.ValueKind != JsonValueKind.Object)
        {
            return byMonitor;
        }

        if (value.TryGetProperty("width", out _))
        {
            var legacy = value.Deserialize<WindowPlacementRecord>(JsonOptions);
            var monitor = value.TryGetProperty("monitorDeviceName", out var name)
                ? name.GetString()
                : null;
            if (legacy != null && !string.IsNullOrWhiteSpace(monitor))
            {
                var work = DisplayWorkAreas.FindByDeviceName(monitor);
                if (work != null)
                {
                    var relative = WindowPlacementFit.ToRelative(
                        new DipRect(legacy.Left, legacy.Top, legacy.Width, legacy.Height),
                        work.Value.Bounds);
                    legacy.Left = relative.Left;
                    legacy.Top = relative.Top;
                    legacy.Width = relative.Width;
                    legacy.Height = relative.Height;
                }

                byMonitor[monitor] = legacy;
            }

            return byMonitor;
        }

        foreach (var monitor in value.EnumerateObject())
        {
            var record = monitor.Value.Deserialize<WindowPlacementRecord>(JsonOptions);
            if (record != null)
            {
                byMonitor[monitor.Name] = record;
            }
        }

        return byMonitor;
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(placements, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save WindowPlacements.json.");
        }
    }

    private static WindowPlacementRecord Clone(WindowPlacementRecord record)
    {
        return new WindowPlacementRecord
        {
            Left = record.Left,
            Top = record.Top,
            Width = record.Width,
            Height = record.Height,
            WindowState = record.WindowState
        };
    }
}
