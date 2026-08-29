namespace MyTools.Common.Plugins;

/// <summary>
/// Stable plugin identity. Equality is case-insensitive.
/// </summary>
public sealed record PluginId
{
    public string Value { get; }

    public PluginId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    public bool Equals(PluginId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
