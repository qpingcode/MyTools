using System.Runtime.InteropServices;

namespace MyTools.Plugins;

public class FileLinkOpenHelper
{
    private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    
    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpVerb;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpFile;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpParameters;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPTStr)]
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    public static void OpenLink(string linkPath, bool runAsAdmin)
    {
        var targetPath =LnkParser.GetTargetPath(linkPath);
        if (string.IsNullOrEmpty(targetPath))
        {
            Console.WriteLine("无法解析.lnk文件的目标目录");
            return;
        }
        
        SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO();
        sei.cbSize = Marshal.SizeOf(sei);
        sei.lpVerb = runAsAdmin ? "runas" : "open";
        sei.lpFile = linkPath;
        sei.lpDirectory = targetPath; // 解决占用MyTools目录,导致启动外部程序后导致无法删除Mytools目录
        sei.nShow = 1; // SW_SHOWNORMAL

        if (!ShellExecuteEx(ref sei))
        {
            throw new Exception("Failed to open the .lnk file.");
        }
    }
}