using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MyTools.Desktop.Views;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Manifest;

namespace MyTools.Desktop.Services;

public sealed class PathPluginHostCallHandler : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly Func<JsonElement, CancellationToken, Task<JsonElement>> _pickPathAsync;

    public PathPluginHostCallHandler()
        : this(PickPathAsync)
    {
    }

    internal PathPluginHostCallHandler(
        Func<JsonElement, CancellationToken, Task<JsonElement>> pickPathAsync)
    {
        _pickPathAsync = pickPathAsync;
    }

    public IReadOnlyCollection<string> Capabilities { get; } = ["path.pick", "path.validate"];

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "path.pick" => await _pickPathAsync(request.Params, cancellationToken),
            "path.validate" => ValidatePath(request.Params),
            _ => throw new NotSupportedException($"Unknown path hostCall method: {request.Method}")
        };
    }

    private static async Task<JsonElement> PickPathAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var request = payload.Deserialize<PickPathRequest>(JsonOptions) ?? new PickPathRequest();
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF dispatcher is not available.");
        var kind = PluginConfigurationTypes.NormalizePathKind(request.Kind);

        var selectedPath = await dispatcher.InvokeAsync(() =>
        {
            var searchWindow = Application.Current.Windows
                .OfType<SearchWindow>()
                .FirstOrDefault(window => window.IsVisible && window.IsActive);
            using var autoHide = searchWindow?.SuppressAutoHide();
            return kind == PluginConfigurationTypes.PathDirectory
                ? PickDirectory(request, searchWindow)
                : PickFile(request, checkFileExists: true, searchWindow);
        }, DispatcherPriority.Normal, cancellationToken);

        return JsonSerializer.SerializeToElement(new
        {
            cancelled = string.IsNullOrWhiteSpace(selectedPath),
            path = selectedPath
        }, JsonOptions);
    }

    private static string? PickDirectory(PickPathRequest request, Window? owner)
    {
        var dialog = new OpenFolderDialog
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Select folder" : request.Title,
            Multiselect = false
        };
        if (Directory.Exists(request.InitialPath))
        {
            dialog.InitialDirectory = request.InitialPath;
        }
        else if (File.Exists(request.InitialPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(request.InitialPath) ?? string.Empty;
        }

        return (owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true
            ? dialog.FolderName
            : null;
    }

    private static string? PickFile(PickPathRequest request, bool checkFileExists, Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Select file" : request.Title,
            Filter = string.IsNullOrWhiteSpace(request.Filter)
                ? "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
                : request.Filter,
            CheckFileExists = checkFileExists,
            Multiselect = false
        };

        if (Directory.Exists(request.InitialPath))
        {
            dialog.InitialDirectory = request.InitialPath;
        }
        else if (File.Exists(request.InitialPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(request.InitialPath) ?? string.Empty;
            dialog.FileName = Path.GetFileName(request.InitialPath);
        }

        return (owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true
            ? dialog.FileName
            : null;
    }

    private static JsonElement ValidatePath(JsonElement payload)
    {
        var request = payload.Deserialize<ValidatePathRequest>(JsonOptions) ?? new ValidatePathRequest();
        var validation = ValidatePathByKind(request.Path, request.Kind);
        return JsonSerializer.SerializeToElement(new
        {
            valid = validation.IsValid,
            message = validation.Message
        }, JsonOptions);
    }

    public static PathValidationResult ValidatePathByKind(string? pathText, string? kind)
    {
        var value = pathText?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value))
        {
            return PathValidationResult.Valid();
        }

        if (!Path.IsPathRooted(value))
        {
            return PathValidationResult.Invalid("Path must be an absolute path.");
        }

        var fileExists = File.Exists(value);
        var directoryExists = Directory.Exists(value);
        if (!fileExists && !directoryExists)
        {
            return PathValidationResult.Invalid("Path does not exist.");
        }

        var normalizedKind = PluginConfigurationTypes.NormalizePathKind(kind);
        if (normalizedKind == PluginConfigurationTypes.PathFile && !fileExists)
        {
            return PathValidationResult.Invalid("Please select an existing file path.");
        }

        if (normalizedKind == PluginConfigurationTypes.PathDirectory && !directoryExists)
        {
            return PathValidationResult.Invalid("Please select an existing folder path.");
        }

        return PathValidationResult.Valid();
    }
}

public sealed class PickPathRequest
{
    public string? Title { get; init; }
    public string? Filter { get; init; }
    public string? InitialPath { get; init; }
    public string? Kind { get; init; }
}

public sealed class ValidatePathRequest
{
    public string? Path { get; init; }
    public string? Kind { get; init; }
}

public sealed class PathValidationResult
{
    public bool IsValid { get; init; }
    public string? Message { get; init; }

    public static PathValidationResult Valid() => new() { IsValid = true };
    public static PathValidationResult Invalid(string message) => new() { IsValid = false, Message = message };
}
