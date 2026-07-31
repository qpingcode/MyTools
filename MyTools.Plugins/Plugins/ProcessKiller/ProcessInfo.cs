using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MyTools.Plugins;

public class ProcessInfo
{
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool LookupAccountSid(string? lpSystemName, IntPtr Sid, StringBuilder? lpName, ref int cchName, StringBuilder? lpReferencedDomainName, ref int cchReferencedDomainName, out int peUse);

    private const string Unknown = "Unknown";

    private static string GetProcessUsername(Process process)
    {
        try
        {
            IntPtr tokenHandle = IntPtr.Zero;
            if (!OpenProcessToken(process.Handle, 0x0008, out tokenHandle))
                return Unknown;

            try
            {
                int tokenInfoLength = 0;
                GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenUser, IntPtr.Zero, 0, out tokenInfoLength);
                if (tokenInfoLength == 0)
                    return Unknown;

                IntPtr tokenInfo = Marshal.AllocHGlobal(tokenInfoLength);
                try
                {
                    if (!GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenUser, tokenInfo, tokenInfoLength, out tokenInfoLength))
                        return Unknown;

                    TOKEN_USER tokenUser = (TOKEN_USER)Marshal.PtrToStructure(tokenInfo, typeof(TOKEN_USER))!;
                    int nameLength = 0;
                    int domainLength = 0;
                    LookupAccountSid(null, tokenUser.User.Sid, null, ref nameLength, null, ref domainLength, out _);

                    if (nameLength == 0)
                        return Unknown;

                    StringBuilder? name = new StringBuilder(nameLength);
                    StringBuilder? domain = new StringBuilder(domainLength);
                    if (!LookupAccountSid(null, tokenUser.User.Sid, name, ref nameLength, domain, ref domainLength, out _))
                        return Unknown;

                    return $"{domain}\\{name}";
                }
                finally
                {
                    Marshal.FreeHGlobal(tokenInfo);
                }
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return Unknown;
        }
    }

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long MemoryUsage { get; set; }
    public DateTime StartTime { get; set; }
    public string UserName { get; set; } = string.Empty;

    public int Port { get; set; } = -1;

    public string DisplayTitle
    {
        get
        {
            var title = Name;
            if (!string.IsNullOrEmpty(Title))
            {
                title += $" - {Title}";
            }
            return title;
        }
    }

    public string DisplaySubTitle
    {
        get
        {
            var subtitle = $"PID: {Id} | User: {UserName}";
            if (Port > 0)
            {
                subtitle += $" | Port: {Port}";
            }
            if (!string.IsNullOrEmpty(FilePath))
            {
                subtitle += $" | Path: {FilePath}";
            }
            return subtitle;
        }
    }

    public static ProcessInfo FromProcess(Process process, int port)
    {
        try
        {
            return new ProcessInfo
            {
                Id = process.Id,
                Name = process.ProcessName,
                Title = process.MainWindowTitle,
                FilePath = process.MainModule?.FileName ?? string.Empty,
                MemoryUsage = process.WorkingSet64,
                StartTime = process.StartTime,
                UserName = GetProcessUsername(process),
                Port = port,
            };
        }
        catch
        {
            return new ProcessInfo
            {
                Id = process.Id,
                Name = process.ProcessName,
                Title = string.Empty,
                FilePath = string.Empty,
                MemoryUsage = process.WorkingSet64,
                StartTime = DateTime.MinValue,
                UserName = Unknown,
                Port = port,
            };
        }
    }
} 