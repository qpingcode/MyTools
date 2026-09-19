using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTools.Protocol.Versioning;

/// <summary>
/// Protocol version declared by the plugin manifest and carried on protocol envelopes.
/// Compatibility is validated before a plugin process starts.
/// </summary>
[JsonConverter(typeof(ProtocolVersionJsonConverter))]
public readonly record struct ProtocolVersion(int Major, int Minor) : IComparable<ProtocolVersion>
{
    /// <summary>The highest version this host supports.</summary>
    public static ProtocolVersion Current { get; } = new(3, 0);

    /// <summary>Wire form of <see cref="Current"/> (currently "3.0").</summary>
    public static string CurrentWire => Current.ToString();

    public override string ToString() => $"{Major}.{Minor}";

    public int CompareTo(ProtocolVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0 ? majorComparison : Minor.CompareTo(other.Minor);
    }

    public static ProtocolVersion Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("version string is empty", nameof(text));
        }
        var dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1)
        {
            throw new ArgumentException($"version '{text}' is not major.minor", nameof(text));
        }
        if (!int.TryParse(text.AsSpan(0, dot), out var major) ||
            !int.TryParse(text.AsSpan(dot + 1), out var minor))
        {
            throw new ArgumentException($"version '{text}' has non-numeric components", nameof(text));
        }
        return new ProtocolVersion(major, minor);
    }
}

/// <summary>Wire format is the string "major.minor" (e.g. "3.0").</summary>
public sealed class ProtocolVersionJsonConverter : JsonConverter<ProtocolVersion>
{
    public override ProtocolVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ProtocolVersion.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, ProtocolVersion value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
