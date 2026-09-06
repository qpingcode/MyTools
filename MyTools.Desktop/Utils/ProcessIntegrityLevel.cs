using System.Runtime.InteropServices;

namespace MyTools.Desktop.Utils;

internal static class ProcessIntegrityLevel
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;

    public static string Read(uint processId)
    {
        if (processId == 0)
        {
            return "none";
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            return $"unavailable:{Marshal.GetLastWin32Error()}";
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return $"unavailable:{Marshal.GetLastWin32Error()}";
            }

            try
            {
                _ = GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var requiredSize);
                if (requiredSize == 0)
                {
                    return $"unavailable:{Marshal.GetLastWin32Error()}";
                }

                var buffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, requiredSize, out _))
                    {
                        return $"unavailable:{Marshal.GetLastWin32Error()}";
                    }

                    var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                    var countPointer = GetSidSubAuthorityCount(label.Label.Sid);
                    if (countPointer == IntPtr.Zero)
                    {
                        return "unavailable:sid";
                    }

                    var count = Marshal.ReadByte(countPointer);
                    if (count == 0)
                    {
                        return "unavailable:sid";
                    }

                    var ridPointer = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
                    return ridPointer == IntPtr.Zero
                        ? "unavailable:sid"
                        : DescribeRid((uint)Marshal.ReadInt32(ridPointer));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                _ = CloseHandle(token);
            }
        }
        finally
        {
            _ = CloseHandle(process);
        }
    }

    internal static string DescribeRid(uint rid) => rid switch
    {
        < 0x1000 => "untrusted",
        < 0x2000 => "low",
        < 0x2100 => "medium",
        < 0x3000 => "medium-plus",
        < 0x4000 => "high",
        < 0x5000 => "system",
        _ => "protected"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel
    {
        public SidAndAttributes Label;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthority);
}
