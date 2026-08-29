using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MyTools.Host.Core.Security;

/// <summary>
/// Identity of the Node process the host expects to connect. Combines the PID, the process
/// creation time (defends against PID reuse) and the plugin the connection must claim.
/// </summary>
public sealed record ProcessIdentity(
    int Pid,
    DateTime CreationTime,
    string PluginId);

/// <summary>
/// A short-lived one-shot bootstrap token. Per the design: the host generates it, passes it to the
/// Node via the stdin first line (not via command-line args or env vars), and it is invalidated
/// immediately after a successful handshake. Carries a short TTL so a leaked token is useless.
/// </summary>
public sealed record BootstrapToken
{
    public string Value { get; init; } = "";
    public string PluginId { get; init; } = "";
    public int ExpectedPid { get; init; }
    public DateTime ExpectedCreationTime { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>Result of validating a presented token value against the expected identity.</summary>
public readonly record struct TokenValidation(bool IsValid, string? Reason)
{
    public static TokenValidation Ok() => new(true, null);
    public static TokenValidation Fail(string reason) => new(false, reason);
}

/// <summary>
/// Issues and validates one-shot bootstrap tokens. The host issues a token (remembering its secret
/// value + bound identity) and hands the value to the Node via stdin; the Node presents that value
/// during handshake. A token is accepted only when the value matches a known unexpired, unconsumed
/// token whose PID + creation time + plugin match the presenting process.
/// </summary>
public sealed class BootstrapTokenValidator
{
    private readonly Func<DateTime> _clock;
    // Shared across plugin startups; Issue/Validate run on many threads at once.
    private readonly ConcurrentDictionary<string, BootstrapToken> _issued = new();

    public BootstrapTokenValidator(Func<DateTime>? clock = null)
        => _clock = clock ?? (() => DateTime.UtcNow);

    /// <summary>Issues a token for the given identity and TTL, remembering its secret value.</summary>
    public BootstrapToken Issue(ProcessIdentity identity, TimeSpan ttl)
    {
        var now = _clock();
        var token = new BootstrapToken
        {
            Value = RandomNumberGenerator.GetBytes(32).ToHexString(),
            PluginId = identity.PluginId,
            ExpectedPid = identity.Pid,
            ExpectedCreationTime = identity.CreationTime,
            IssuedAt = now,
            ExpiresAt = now + ttl,
        };
        _issued[token.Value] = token;
        return token;
    }

    /// <summary>
    /// Validates the value presented by a process against the remembered token. On success the
    /// token is consumed (one-shot) and cannot be reused.
    /// </summary>
    public TokenValidation Validate(string presentedValue, ProcessIdentity observed)
    {
        if (string.IsNullOrEmpty(presentedValue))
        {
            return TokenValidation.Fail("presented token value is empty");
        }
        if (!_issued.TryGetValue(presentedValue, out var token))
        {
            return TokenValidation.Fail("token value not recognized");
        }
        var now = _clock();

        if (now > token.ExpiresAt)
        {
            _issued.TryRemove(presentedValue, out _);
            return TokenValidation.Fail("token expired");
        }
        if (token.ExpectedPid != observed.Pid)
        {
            return TokenValidation.Fail($"pid mismatch: expected {token.ExpectedPid}, got {observed.Pid}");
        }
        if (token.ExpectedCreationTime != observed.CreationTime)
        {
            return TokenValidation.Fail("process creation time mismatch (possible PID reuse)");
        }
        if (token.PluginId != observed.PluginId)
        {
            return TokenValidation.Fail("plugin identity mismatch");
        }

        // one-shot: consume on success. TryRemove loses to a concurrent consumer.
        if (!_issued.TryRemove(presentedValue, out _))
        {
            return TokenValidation.Fail("token value not recognized");
        }
        return TokenValidation.Ok();
    }
}

internal static class ByteArrayExtensions
{
    public static string ToHexString(this byte[] bytes)
    {
        var s = new char[bytes.Length * 2];
        const string hex = "0123456789abcdef";
        for (var i = 0; i < bytes.Length; i++)
        {
            s[i * 2] = hex[bytes[i] >> 4];
            s[i * 2 + 1] = hex[bytes[i] & 0xF];
        }
        return new string(s);
    }
}
