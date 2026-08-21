using MyTools.Common;

namespace MyTools.Plugins.Param;

public sealed class ExecuteActionParams(string path, string arguments = "") : IActionStringParam
{
    public string Arguments { get; } = arguments ?? string.Empty;

    public string GetValue() => path;
}
