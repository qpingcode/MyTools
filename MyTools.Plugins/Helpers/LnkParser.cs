using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MyTools.Plugins;

public static class LnkParser
{
    /// <summary>
    /// 解析.lnk文件，获取目标程序的路径
    /// </summary>
    /// <param name="lnkPath">.lnk文件的完整路径</param>
    /// <returns>目标程序的路径（如：C:\Program Files\SSMS\ssms.exe）</returns>
    public static string? GetTargetPath(string lnkPath)
    {
        if (!File.Exists(lnkPath) || !lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("无效的.lnk文件路径", nameof(lnkPath));
        }

        // 创建IShellLink接口实例
        var shellLink = (IShellLink)new ShellLink();
        
        try
        {
            // 从.lnk文件加载信息
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(lnkPath, STGM_READ);

            // 获取目标路径（最多MAX_PATH长度）
            var targetPath = new StringBuilder(MAX_PATH);
            var result = shellLink.GetPath(targetPath, MAX_PATH, IntPtr.Zero, SLGP_SHORTPATH);
            
            return result == 0 ? targetPath.ToString() : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"解析.lnk文件失败：{ex.Message}");
            return null;
        }
        finally
        {
            // 释放COM对象
            Marshal.ReleaseComObject(shellLink);
        }
    }

    /// <summary>
    /// 解析快捷方式用于取图标：优先自定义图标，否则用目标文件（避免 Shell 叠快捷方式箭头）。
    /// </summary>
    public static LnkIconSource? TryGetIconSource(string lnkPath)
    {
        if (string.IsNullOrWhiteSpace(lnkPath)
            || !lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(lnkPath))
        {
            return null;
        }

        var shellLink = (IShellLink)new ShellLink();
        try
        {
            var persistFile = (IPersistFile)shellLink;
            persistFile.Load(lnkPath, STGM_READ);

            string? customIconPath = null;
            var customIconIndex = 0;
            var iconLocation = new StringBuilder(MAX_PATH);
            try
            {
                shellLink.GetIconLocation(iconLocation, MAX_PATH, out customIconIndex);
                var raw = iconLocation.ToString().Trim();
                if (!string.IsNullOrEmpty(raw))
                {
                    customIconPath = ResolveIconPath(lnkPath, Environment.ExpandEnvironmentVariables(raw));
                }
            }
            catch
            {
                // Some shortcuts omit icon location; fall back to the target.
            }

            var targetPath = ReadTargetPath(shellLink);
            if (string.IsNullOrEmpty(customIconPath) && string.IsNullOrEmpty(targetPath))
            {
                return null;
            }

            return new LnkIconSource(customIconPath, customIconIndex, targetPath);
        }
        catch
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(shellLink);
        }
    }

    private static string? ReadTargetPath(IShellLink shellLink)
    {
        var targetPath = new StringBuilder(MAX_PATH);
        if (shellLink.GetPath(targetPath, MAX_PATH, IntPtr.Zero, SLGP_RAWPATH) == 0)
        {
            var raw = targetPath.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
        }

        targetPath.Clear();
        if (shellLink.GetPath(targetPath, MAX_PATH, IntPtr.Zero, 0) == 0)
        {
            var raw = targetPath.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }

        return null;
    }

    private static string ResolveIconPath(string lnkPath, string iconPath)
    {
        if (Path.IsPathRooted(iconPath))
        {
            return iconPath;
        }

        var lnkDirectory = Path.GetDirectoryName(lnkPath);
        return string.IsNullOrEmpty(lnkDirectory)
            ? iconPath
            : Path.GetFullPath(Path.Combine(lnkDirectory, iconPath));
    }

    /// <summary>
    /// 解析.lnk文件，获取目标程序所在的目录
    /// </summary>
    /// <param name="lnkPath">.lnk文件的完整路径</param>
    /// <returns>目标程序所在的目录（如：C:\Program Files\SSMS）</returns>
    public static string? GetTargetDirectory(string lnkPath)
    {
        var targetPath = GetTargetPath(lnkPath);
        if (string.IsNullOrEmpty(targetPath))
            return null;

        // 从目标路径中提取目录（如从ssms.exe路径中提取其所在文件夹）
        return Path.GetDirectoryName(targetPath);
    }

    // 常量定义
    private const int MAX_PATH = 260;
    private const uint STGM_READ = 0x00000000;
    private const uint SLGP_SHORTPATH = 0x00000001;
    private const uint SLGP_RAWPATH = 0x00000004;

    // COM接口定义（用于解析.lnk文件）
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        [PreserveSig]
        int GetPath([Out] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out] StringBuilder pszName, int cchMaxName);
        void SetDescription(string pszName);
        void GetWorkingDirectory([Out] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory(string pszDir);
        void GetArguments([Out] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments(string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation(string pszIconPath, int iIcon);
        void SetRelativePath(string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath(string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        [PreserveSig]
        int Load(string pszFileName, uint dwMode);
        [PreserveSig]
        int Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        [PreserveSig]
        int SaveCompleted(string pszFileName);
        [PreserveSig]
        int GetCurFile(out string ppszFileName);
    }
}

public sealed record LnkIconSource(string? CustomIconPath, int CustomIconIndex, string? TargetPath)
{
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(CustomIconPath);
}