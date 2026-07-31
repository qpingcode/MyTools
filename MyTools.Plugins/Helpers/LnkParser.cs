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