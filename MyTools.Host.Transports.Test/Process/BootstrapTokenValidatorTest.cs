using System;
using MyTools.Host.Core.Security;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.Process;

[TestFixture]
public class BootstrapTokenValidatorTest
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ProcessIdentity NodeIdentity =
        new(Pid: 4321, CreationTime: BaseTime, PluginId: "settings", EntryId: "main");

    private static BootstrapTokenValidator IssueWithClock(Func<DateTime> clock, TimeSpan ttl)
    {
        var v = new BootstrapTokenValidator(clock);
        v.Issue(NodeIdentity, ttl);
        return v;
    }

    [Test]
    public void Issue_ShouldPopulateValueAndIdentity()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));

        Assert.That(token.Value, Is.Not.Null.And.Not.Empty);
        Assert.That(token.PluginId, Is.EqualTo("settings"));
        Assert.That(token.EntryId, Is.EqualTo("main"));
        Assert.That(token.ExpectedPid, Is.EqualTo(4321));
    }

    [Test]
    public void Validate_CorrectValueBeforeExpiry_ShouldSucceed()
    {
        var v = IssueWithClock(() => BaseTime, TimeSpan.FromSeconds(30));
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        // Advance the validator's clock forward but within TTL.
        var later = new BootstrapTokenValidator(() => BaseTime.AddSeconds(10));
        var issued = later.Issue(NodeIdentity, TimeSpan.FromSeconds(30));

        var result = later.Validate(issued.Value, NodeIdentity);

        Assert.That(result.IsValid, Is.True);
        _ = token; // unused; kept for clarity
    }

    [Test]
    public void Validate_AfterExpiry_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        // A validator whose clock is past expiry.
        var expired = new BootstrapTokenValidator(() => BaseTime.AddSeconds(60));
        expired._RegisterForTest(token);

        var result = expired.Validate(token.Value, NodeIdentity);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Reason, Does.Contain("expired"));
    }

    [Test]
    public void Validate_TokenAlreadyConsumed_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        v.Validate(token.Value, NodeIdentity); // first use consumes it

        var second = v.Validate(token.Value, NodeIdentity);

        Assert.That(second.IsValid, Is.False);
        Assert.That(second.Reason, Does.Contain("recognized").Or.Contains("consumed"));
    }

    [Test]
    public void Validate_WrongValue_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));

        var result = v.Validate("not-the-issued-value", NodeIdentity);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Reason, Does.Contain("value").Or.Contains("recognized"));
    }

    [Test]
    public void Validate_WrongPid_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        var impostor = NodeIdentity with { Pid = 9999 };

        var result = v.Validate(token.Value, impostor);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Reason, Does.Contain("pid"));
    }

    [Test]
    public void Validate_WrongPluginId_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        var impostor = NodeIdentity with { PluginId = "evil" };

        var result = v.Validate(token.Value, impostor);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public void Validate_WrongCreationTime_ShouldFail()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        var token = v.Issue(NodeIdentity, TimeSpan.FromSeconds(30));
        var reused = NodeIdentity with { CreationTime = BaseTime.AddHours(1) };

        var result = v.Validate(token.Value, reused);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Reason, Does.Contain("creation"));
    }

    [Test]
    public void IssueAndValidate_FromManyThreads_ShouldKeepTokensRecognized()
    {
        var v = new BootstrapTokenValidator(() => BaseTime);
        const int n = 64;
        Parallel.For(0, n, i =>
        {
            var identity = new ProcessIdentity(i + 1, BaseTime, $"plugin-{i}", "main");
            var token = v.Issue(identity, TimeSpan.FromMinutes(1));
            var result = v.Validate(token.Value, identity);
            Assert.That(result.IsValid, Is.True, result.Reason);
        });
    }
}

internal static class BootstrapTokenValidatorTestExtensions
{
    // Test-only helper to register a pre-issued token into a validator (for expiry tests where the
    // issuing clock and the validating clock differ).
    public static void _RegisterForTest(this BootstrapTokenValidator v, BootstrapToken token)
        => v.GetType()
            .GetField("_issued", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(v, new System.Collections.Concurrent.ConcurrentDictionary<string, BootstrapToken>
            {
                [token.Value] = token,
            });
}
