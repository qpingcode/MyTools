using System.Reflection;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Helpers;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class LnkIconSourceTests
{
    [Test]
    public void TryGetIconSource_UsesTargetWhenShortcutHasNoCustomIcon()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.That(File.Exists(notepad), Is.True);

        var lnkPath = CreateShortcut(notepad, iconLocation: null);
        try
        {
            var source = LnkParser.TryGetIconSource(lnkPath);

            Assert.That(source, Is.Not.Null);
            Assert.That(source!.HasCustomIcon, Is.False);
            Assert.That(source.TargetPath, Is.EqualTo(notepad).IgnoreCase);
        }
        finally
        {
            File.Delete(lnkPath);
        }
    }

    [Test]
    public void TryGetIconSource_UsesCustomIconLocationWhenPresent()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var shell32 = Path.Combine(Environment.SystemDirectory, "shell32.dll");
        Assert.That(File.Exists(shell32), Is.True);

        var lnkPath = CreateShortcut(notepad, iconLocation: $"{shell32},0");
        try
        {
            var source = LnkParser.TryGetIconSource(lnkPath);

            Assert.That(source, Is.Not.Null);
            Assert.That(source!.HasCustomIcon, Is.True);
            Assert.That(source.CustomIconPath, Is.EqualTo(shell32).IgnoreCase);
            Assert.That(source.CustomIconIndex, Is.EqualTo(0));
        }
        finally
        {
            File.Delete(lnkPath);
        }
    }

    [Test]
    public void GetFileIconData_ShortcutWithoutCustomIcon_DoesNotUseLnkOverlaySource()
    {
        var notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var lnkPath = CreateShortcut(notepad, iconLocation: null);
        try
        {
            var source = LnkParser.TryGetIconSource(lnkPath);
            var icon = FileIconHelper.GetFileIconData(lnkPath);

            Assert.That(source?.TargetPath, Is.EqualTo(notepad).IgnoreCase);
            Assert.That(icon, Is.Not.Null.And.Not.Empty);
        }
        finally
        {
            File.Delete(lnkPath);
        }
    }

    private static string CreateShortcut(string targetPath, string? iconLocation)
    {
        var lnkPath = Path.Combine(Path.GetTempPath(), $"mytools-icon-{Guid.NewGuid():N}.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        var shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        var shortcut = shellType.InvokeMember(
            "CreateShortcut",
            BindingFlags.InvokeMethod,
            binder: null,
            target: shell,
            args: [lnkPath]);
        Assert.That(shortcut, Is.Not.Null);

        var shortcutType = shortcut!.GetType();
        shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);
        if (!string.IsNullOrWhiteSpace(iconLocation))
        {
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [iconLocation]);
        }

        shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        return lnkPath;
    }
}
