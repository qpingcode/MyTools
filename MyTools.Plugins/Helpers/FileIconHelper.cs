using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MyTools.Plugins;

public static class FileIconHelper
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FileAttributeNormal = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        IntPtr[]? phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public static byte[]? GetFileIconData(string filePath, bool isLargeIcon = true)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("文件路径不能为空。");

        if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var withoutOverlay = TryGetShortcutIconWithoutOverlay(filePath, isLargeIcon);
            if (withoutOverlay != null)
            {
                return withoutOverlay;
            }
        }

        return TryGetShellIcon(filePath, isLargeIcon, useFileAttributes: true);
    }

    private static byte[]? TryGetShortcutIconWithoutOverlay(string lnkPath, bool isLargeIcon)
    {
        var source = LnkParser.TryGetIconSource(lnkPath);
        if (source == null)
        {
            return null;
        }

        if (source.HasCustomIcon)
        {
            var custom = TryExtractIcon(source.CustomIconPath!, source.CustomIconIndex, isLargeIcon);
            if (custom != null)
            {
                return custom;
            }
        }

        if (!string.IsNullOrWhiteSpace(source.TargetPath))
        {
            return TryGetShellIcon(source.TargetPath, isLargeIcon, useFileAttributes: false);
        }

        return null;
    }

    private static byte[]? TryExtractIcon(string iconFile, int iconIndex, bool isLargeIcon)
    {
        if (!File.Exists(iconFile))
        {
            return null;
        }

        var large = new IntPtr[1];
        var small = new IntPtr[1];
        var extracted = ExtractIconEx(iconFile, iconIndex, large, small, 1);
        var handle = isLargeIcon ? large[0] : small[0];
        var other = isLargeIcon ? small[0] : large[0];
        try
        {
            if (extracted == 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return IconHandleToPng(handle);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                DestroyIcon(handle);
            }

            if (other != IntPtr.Zero)
            {
                DestroyIcon(other);
            }
        }
    }

    private static byte[]? TryGetShellIcon(string filePath, bool isLargeIcon, bool useFileAttributes)
    {
        var flags = SHGFI_ICON;
        flags |= isLargeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON;
        if (useFileAttributes)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        if (!TryGetFileInfoSave(filePath, flags, useFileAttributes, out var shInfo))
        {
            return null;
        }

        try
        {
            return IconHandleToPng(shInfo.hIcon);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (shInfo.hIcon != IntPtr.Zero)
            {
                DestroyIcon(shInfo.hIcon);
            }
        }
    }

    private static byte[] IconHandleToPng(IntPtr hIcon)
    {
        using var icon = Icon.FromHandle(hIcon);
        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static readonly object LockObject = new();

    private static bool TryGetFileInfoSave(
        string filePath,
        uint flags,
        bool useFileAttributes,
        out SHFILEINFO result)
    {
        result = new SHFILEINFO();
        var size = (uint)Marshal.SizeOf(result);

        lock (LockObject)
        {
            var ptr = SHGetFileInfo(
                filePath,
                useFileAttributes ? FileAttributeNormal : 0,
                ref result,
                size,
                flags);

            if (ptr == IntPtr.Zero)
            {
                return false;
            }
        }

        return result.hIcon != IntPtr.Zero;
    }
}
