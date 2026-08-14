namespace MyTools.Protocol.Versioning;

/// <summary>
/// Protocol version with frozen major/minor semantics. Major mismatch is fatal (ProtocolMismatch);
/// minor is negotiated during handshake to the highest common value.
/// </summary>
public readonly record struct ProtocolVersion(int Major, int Minor) : IComparable<ProtocolVersion>
{
    /// <summary>The highest version this host supports.</summary>
    public static ProtocolVersion Current { get; } = new(3, 0);

    public override string ToString() => $"{Major}.{Minor}";

    public int CompareTo(ProtocolVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        return majorComparison != 0 ? majorComparison : Minor.CompareTo(other.Minor);
    }

    /// <summary>True when both sides share the same major version (the only compatibility gate).</summary>
    public bool IsMajorCompatibleWith(ProtocolVersion other) => Major == other.Major;

    /// <summary>
    /// Returns the highest minor version present in <paramref name="theirs"/> that is both
    /// ≤ this version and major-compatible, or <see langword="null"/> when no common minor exists.
    /// </summary>
    public (int major, int minor)? HighestCommonMinor(IEnumerable<ProtocolVersion> theirs)
    {
        var best = -1;
        foreach (var t in theirs)
        {
            if (t.Major != Major) continue;
            if (t.Minor > Minor) continue;
            if (t.Minor > best) best = t.Minor;
        }
        return best >= 0 ? (Major, best) : null;
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
