using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class PathPluginHostCallHandler : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyCollection<string> Capabilities { get; } = ["path.pick", "path.validate"];

    public Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        var result = request.Method switch
        {
            "path.pick" => PickPath(request.Params),
            "path.validate" => ValidatePath(request.Params),
            _ => throw new NotSupportedException($"Unknown path hostCall method: {request.Method}")
        };
        return Task.FromResult(result);
    }

    private static JsonElement PickPath(JsonElement payload)
    {
        var request = payload.Deserialize<PickPathRequest>(JsonOptions) ?? new PickPathRequest();
        var dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF dispatcher is not available.");

        string? selectedPath = null;
        dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "Select file" : request.Title,
                Filter = string.IsNullOrWhiteSpace(request.Filter)
                    ? "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
                    : request.Filter,
                CheckFileExists = true,
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

            if (dialog.ShowDialog() == true)
            {
                selectedPath = dialog.FileName;
            }
        });

        return JsonSerializer.SerializeToElement(new
        {
            cancelled = string.IsNullOrWhiteSpace(selectedPath),
            path = selectedPath
        }, JsonOptions);
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

    internal static PathValidationResult ValidatePathByKind(string? pathText, string? kind)
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

        if (string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase) && !fileExists)
        {
            return PathValidationResult.Invalid("Please select an existing file path.");
        }

        return PathValidationResult.Valid();
    }
}

public sealed class PickPathRequest
{
    public string? Title { get; init; }
    public string? Filter { get; init; }
    public string? InitialPath { get; init; }
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
