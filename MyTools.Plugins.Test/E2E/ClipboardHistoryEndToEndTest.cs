using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common;
using MyTools.Common.WindowsMessageHandler;
using NUnit.Framework;

namespace MyTools.Plugins.Test.E2E;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ClipboardHistoryEndToEndTest
{
    private string tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [Test]
    public void CopyGuidThenOpenClipboardHistory_ShouldShowGuidFirst()
    {
        var guid = Guid.NewGuid().ToString();
        var plugin = new ClipBoardPlugin(
            NullLogger<ClipBoardPlugin>.Instance,
            Path.Combine(tempDirectory, "clipboard_history.db"));

        plugin.InitializeAsync().GetAwaiter().GetResult();
        plugin.AddTextHistoryAsync(["older clipboard item"]).GetAwaiter().GetResult();

        NativeClipboard.SetText(guid);
        var handled = false;
        ((IWindowMessageHandler)plugin).Handle(
            (int)WindowsMessageType.ClipboardUpdate,
            IntPtr.Zero,
            IntPtr.Zero,
            ref handled);

        var clipboardHistory = plugin.SearchAsync(
                string.Empty,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(clipboardHistory.Success, Is.True);
            Assert.That(clipboardHistory.Items.First().Title, Is.EqualTo(guid));
        });
    }

    private static class NativeClipboard
    {
        private const uint CfUnicodeText = 13;
        private const uint GmemMoveable = 0x0002;

        public static void SetText(string text)
        {
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (memory == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var clipboardOwnsMemory = false;
            try
            {
                var target = GlobalLock(memory);
                if (target == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    Marshal.Copy(bytes, 0, target, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(memory);
                }

                OpenWithRetry();
                try
                {
                    if (!EmptyClipboard() || SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    clipboardOwnsMemory = true;
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                if (!clipboardOwnsMemory)
                {
                    GlobalFree(memory);
                }
            }
        }

        private static void OpenWithRetry()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    return;
                }

                Thread.Sleep(50);
            }

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr newOwner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
