using System.Text.Json;
using MyTools.Desktop.Services;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class PathPluginHostCallHandlerTest
{
    [Test]
    public async Task HandleAsync_PathPick_ShouldRemainPendingWhileDialogIsOpen()
    {
        var dialogResult = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PathPluginHostCallHandler((_, _) => dialogResult.Task);
        var request = new HostCallRequest(
            "path.pick",
            JsonSerializer.SerializeToElement(new { kind = PluginConfigurationTypes.PathDirectory }));

        var pendingCall = handler.HandleAsync(request, CancellationToken.None);

        Assert.That(pendingCall.IsCompleted, Is.False,
            "path.pick must yield while the modal dialog is open so the pipe can keep reading heartbeats");

        var expected = JsonSerializer.SerializeToElement(new { cancelled = true, path = (string?)null });
        dialogResult.SetResult(expected);
        var actual = await pendingCall;

        Assert.That(actual.GetProperty("cancelled").GetBoolean(), Is.True);
    }

    [Test]
    public void ValidatePathByKind_Empty_ShouldBeValid()
    {
        var result = PathPluginHostCallHandler.ValidatePathByKind("", PluginConfigurationTypes.PathDirectory);
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidatePathByKind_Relative_ShouldBeInvalid()
    {
        var result = PathPluginHostCallHandler.ValidatePathByKind("relative\\folder", PluginConfigurationTypes.PathDirectory);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Message, Does.Contain("absolute"));
    }

    [Test]
    public void ValidatePathByKind_Directory_ShouldAcceptExistingFolder()
    {
        var result = PathPluginHostCallHandler.ValidatePathByKind(
            Path.GetTempPath(),
            PluginConfigurationTypes.PathDirectory);
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidatePathByKind_Directory_ShouldRejectFile()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            var result = PathPluginHostCallHandler.ValidatePathByKind(filePath, PluginConfigurationTypes.PathDirectory);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Message, Does.Contain("folder"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public void ValidatePathByKind_File_ShouldRejectFolder()
    {
        var result = PathPluginHostCallHandler.ValidatePathByKind(
            Path.GetTempPath(),
            PluginConfigurationTypes.PathFile);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Message, Does.Contain("file"));
    }
}
