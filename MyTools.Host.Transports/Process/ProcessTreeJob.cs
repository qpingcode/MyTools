using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Process = System.Diagnostics.Process;

namespace MyTools.Host.Transports.Process;

/// <summary>
/// A Windows Job Object used solely for process-tree reclamation (kill-on-close). The host assigns
/// the Node process and any descendants; when the Job handle is closed (host exit / session stop)
/// the whole tree is terminated. Per the design this is NOT a security sandbox — no CPU/memory/quota
/// limits are set; it only guarantees no orphaned child processes survive the host.
/// </summary>
public sealed class ProcessTreeJob : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public ProcessTreeJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null!);
        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr,
                    (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Assigns a process (and its future descendants) to this job.</summary>
    public void Assign(System.Diagnostics.Process process)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ProcessTreeJob));
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "AssignProcessToJobObject failed");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Closing the last handle to a kill-on-close job terminates all assigned processes.
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
        }
    }

    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass,
        IntPtr lpInfo, uint cbInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
