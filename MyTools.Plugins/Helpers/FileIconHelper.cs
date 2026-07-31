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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbSizeFileInfo,
        uint uFlags);

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

        var flags = SHGFI_ICON | SHGFI_USEFILEATTRIBUTES;
        flags |= isLargeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON;

        if (!TryGetFileInfoSave(filePath, flags, out var shInfo))
        {
            return null;
        }
        
        try
        {
            using var icon = Icon.FromHandle(shInfo.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取文件图标失败: {ex.Message}");
            return null;
        }
        finally
        {
            DestroyIcon(shInfo.hIcon);
        }
    }
    
    private static readonly object LockObject = new object();
    private static bool TryGetFileInfoSave(string filePath, uint flags, out SHFILEINFO result)
    {
        result = new SHFILEINFO();
        var size = (uint)Marshal.SizeOf(result);
        
        lock (LockObject)
        {
            IntPtr ptr = SHGetFileInfo(
                filePath,
                0x80, // FILE_ATTRIBUTE_NORMAL
                ref result,
                size,
                flags);

            if (ptr == IntPtr.Zero)
                return false;
        }
        return true;
    }
}