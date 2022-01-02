using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Squirrel
{
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    internal static class NativeMethods
    {
        public static int GetParentProcessId()
        {
            var pbi = new PROCESS_BASIC_INFORMATION();

            //Get a handle to our own process
            IntPtr hProc = OpenProcess((ProcessAccess) 0x001F0FFF, false, Process.GetCurrentProcess().Id);

            try {
                int sizeInfoReturned;
                int queryStatus = NtQueryInformationProcess(hProc, (PROCESSINFOCLASS) 0, ref pbi, pbi.Size, out sizeInfoReturned);
            } finally {
                if (!hProc.Equals(IntPtr.Zero)) {
                    //Close handle and free allocated memory
                    CloseHandle(hProc);
                    hProc = IntPtr.Zero;
                }
            }

            return (int) pbi.InheritedFromUniqueProcessId;
        }

        [DllImport("shell32.dll", SetLastError = true)]
        public static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr LoadLibraryEx(string lpModuleName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResource(IntPtr hModule, string lpName, string lpType);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, string lpType);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResourceEx(IntPtr hModule, string lpType, IntPtr lpName, ushort wLanguage);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindResourceEx(IntPtr hModule, string lpType, string lpName, ushort wLanguage);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint SizeofResource(IntPtr hModule, IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LoadResource(IntPtr hModule, IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LockResource(IntPtr hglobal);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("version.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileVersionInfo(
            string lpszFileName,
            int dwHandleIgnored,
            int dwLen,
            [MarshalAs(UnmanagedType.LPArray)] byte[] lpData);

        [DllImport("version.dll", SetLastError = true)]
        internal static extern int GetFileVersionInfoSize(
            string lpszFileName,
            IntPtr dwHandleIgnored);

        [DllImport("version.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool VerQueryValue(
            byte[] pBlock,
            string pSubBlock,
            out IntPtr pValue,
            out int len);

        [DllImport("psapi.dll", SetLastError = true)]
        internal static extern bool EnumProcesses(
            IntPtr pProcessIds, // pointer to allocated DWORD array
            int cb,
            out int pBytesReturned);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            [In] int justPassZeroHere,
            [Out] StringBuilder lpImageFileName,
            [In][MarshalAs(UnmanagedType.U4)] ref int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(
            ProcessAccess processAccess,
            bool bInheritHandle,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hHandle);

        [DllImport("NTDLL.DLL", SetLastError = true)]
        internal static extern int NtQueryInformationProcess(IntPtr hProcess, PROCESSINFOCLASS pic, ref PROCESS_BASIC_INFORMATION pbi, int cb, out int pSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);

        [DllImport("kernel32.dll", EntryPoint = "GetStdHandle")]
        internal static extern IntPtr GetStdHandle(StandardHandles nStdHandle);

        [DllImport("kernel32.dll", EntryPoint = "AllocConsole")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        internal static extern bool AttachConsole(int pid);

        [DllImport("Kernel32.dll", SetLastError = true)]
        internal static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("Kernel32.dll", SetLastError = true)]
        internal static extern bool UpdateResource(IntPtr handle, string pType, IntPtr pName, short language, [MarshalAs(UnmanagedType.LPArray)] byte[] pData, int dwSize);

        [DllImport("Kernel32.dll", SetLastError = true)]
        internal static extern bool EndUpdateResource(IntPtr handle, bool discard);

#nullable enable
        /// <summary>
        ///     The ApplyDelta function use the specified delta and source files to create a new copy of the target file.
        /// </summary>
        /// <param name="applyFlags">Either DELTA_FLAG_NONE or DELTA_APPLY_FLAG_ALLOW_PA19.</param>
        /// <param name="sourceName">The name of the source file to which the delta is to be applied.</param>
        /// <param name="deltaName">The name of the delta to be applied to the source file.</param>
        /// <param name="targetName">The name of the target file that is to be created.</param>
        /// <returns>
        ///     Returns TRUE on success or FALSE otherwise.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#applydeltaaw
        /// </remarks>
        [DllImport("msdelta.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ApplyDelta(
            [MarshalAs(UnmanagedType.I8)] ApplyFlags applyFlags,
            string sourceName,
            string deltaName,
            string targetName);

        /// <summary>
        ///     The CreateDelta function creates a delta from the specified source and target files and write the output delta to the designated file name.
        /// </summary>
        /// <param name="fileTypeSet">The file type set used for Create.</param>
        /// <param name="setFlags">The file type set used for Create.</param>
        /// <param name="resetFlags">The file type set used for Create.</param>
        /// <param name="sourceName">The file type set used for Create.</param>
        /// <param name="targetName">The name of the target against which the source is compared.</param>
        /// <param name="sourceOptionsName">Reserved. Pass NULL.</param>
        /// <param name="targetOptionsName">Reserved. Pass NULL.</param>
        /// <param name="globalOptions">Reserved. Pass a DELTA_INPUT structure with lpStart set to NULL and uSize set to 0.</param>
        /// <param name="targetFileTime">The time stamp set on the target file after delta Apply. If NULL, the timestamp of the target file during delta Create will be used.</param>
        /// <param name="hashAlgId">ALG_ID of the algorithm to be used to generate the target signature.</param>
        /// <param name="deltaName">The name of the delta file to be created.</param>
        /// <returns>
        ///     Returns TRUE on success or FALSE otherwise.
        /// </returns>
        /// <remarks>
        ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#createdeltaaw
        /// </remarks>
        [DllImport("msdelta.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateDelta(
            [MarshalAs(UnmanagedType.I8)] FileTypeSet fileTypeSet,
            [MarshalAs(UnmanagedType.I8)] CreateFlags setFlags,
            [MarshalAs(UnmanagedType.I8)] CreateFlags resetFlags,
            string sourceName,
            string targetName,
            string? sourceOptionsName,
            string? targetOptionsName,
            DeltaInput globalOptions,
            IntPtr targetFileTime,
            [MarshalAs(UnmanagedType.U4)] HashAlgId hashAlgId,
            string deltaName);
#nullable restore
    }

    [Flags]
    internal enum ProcessAccess : uint
    {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VirtualMemoryOperation = 0x00000008,
        VirtualMemoryRead = 0x00000010,
        VirtualMemoryWrite = 0x00000020,
        DuplicateHandle = 0x00000040,
        CreateProcess = 0x000000080,
        SetQuota = 0x00000100,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        QueryLimitedInformation = 0x00001000,
        Synchronize = 0x00100000
    }

    internal enum PROCESSINFOCLASS : int
    {
        ProcessBasicInformation = 0, // 0, q: PROCESS_BASIC_INFORMATION, PROCESS_EXTENDED_BASIC_INFORMATION
        ProcessQuotaLimits, // qs: QUOTA_LIMITS, QUOTA_LIMITS_EX
        ProcessIoCounters, // q: IO_COUNTERS
        ProcessVmCounters, // q: VM_COUNTERS, VM_COUNTERS_EX
        ProcessTimes, // q: KERNEL_USER_TIMES
        ProcessBasePriority, // s: KPRIORITY
        ProcessRaisePriority, // s: ULONG
        ProcessDebugPort, // q: HANDLE
        ProcessExceptionPort, // s: HANDLE
        ProcessAccessToken, // s: PROCESS_ACCESS_TOKEN
        ProcessLdtInformation, // 10
        ProcessLdtSize,
        ProcessDefaultHardErrorMode, // qs: ULONG
        ProcessIoPortHandlers, // (kernel-mode only)
        ProcessPooledUsageAndLimits, // q: POOLED_USAGE_AND_LIMITS
        ProcessWorkingSetWatch, // q: PROCESS_WS_WATCH_INFORMATION[]; s: void
        ProcessUserModeIOPL,
        ProcessEnableAlignmentFaultFixup, // s: BOOLEAN
        ProcessPriorityClass, // qs: PROCESS_PRIORITY_CLASS
        ProcessWx86Information,
        ProcessHandleCount, // 20, q: ULONG, PROCESS_HANDLE_INFORMATION
        ProcessAffinityMask, // s: KAFFINITY
        ProcessPriorityBoost, // qs: ULONG
        ProcessDeviceMap, // qs: PROCESS_DEVICEMAP_INFORMATION, PROCESS_DEVICEMAP_INFORMATION_EX
        ProcessSessionInformation, // q: PROCESS_SESSION_INFORMATION
        ProcessForegroundInformation, // s: PROCESS_FOREGROUND_BACKGROUND
        ProcessWow64Information, // q: ULONG_PTR
        ProcessImageFileName, // q: UNICODE_STRING
        ProcessLUIDDeviceMapsEnabled, // q: ULONG
        ProcessBreakOnTermination, // qs: ULONG
        ProcessDebugObjectHandle, // 30, q: HANDLE
        ProcessDebugFlags, // qs: ULONG
        ProcessHandleTracing, // q: PROCESS_HANDLE_TRACING_QUERY; s: size 0 disables, otherwise enables
        ProcessIoPriority, // qs: ULONG
        ProcessExecuteFlags, // qs: ULONG
        ProcessResourceManagement,
        ProcessCookie, // q: ULONG
        ProcessImageInformation, // q: SECTION_IMAGE_INFORMATION
        ProcessCycleTime, // q: PROCESS_CYCLE_TIME_INFORMATION
        ProcessPagePriority, // q: ULONG
        ProcessInstrumentationCallback, // 40
        ProcessThreadStackAllocation, // s: PROCESS_STACK_ALLOCATION_INFORMATION, PROCESS_STACK_ALLOCATION_INFORMATION_EX
        ProcessWorkingSetWatchEx, // q: PROCESS_WS_WATCH_INFORMATION_EX[]
        ProcessImageFileNameWin32, // q: UNICODE_STRING
        ProcessImageFileMapping, // q: HANDLE (input)
        ProcessAffinityUpdateMode, // qs: PROCESS_AFFINITY_UPDATE_MODE
        ProcessMemoryAllocationMode, // qs: PROCESS_MEMORY_ALLOCATION_MODE
        ProcessGroupInformation, // q: USHORT[]
        ProcessTokenVirtualizationEnabled, // s: ULONG
        ProcessConsoleHostProcess, // q: ULONG_PTR
        ProcessWindowInformation, // 50, q: PROCESS_WINDOW_INFORMATION
        ProcessHandleInformation, // q: PROCESS_HANDLE_SNAPSHOT_INFORMATION // since WIN8
        ProcessMitigationPolicy, // s: PROCESS_MITIGATION_POLICY_INFORMATION
        ProcessDynamicFunctionTableInformation,
        ProcessHandleCheckingMode,
        ProcessKeepAliveCount, // q: PROCESS_KEEPALIVE_COUNT_INFORMATION
        ProcessRevokeFileHandles, // s: PROCESS_REVOKE_FILE_HANDLES_INFORMATION
        MaxProcessInfoClass
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;

        public int Size {
            get { return (int) Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)); }
        }
    }

    internal enum StandardHandles : int
    {
        STD_INPUT_HANDLE = -10,
        STD_OUTPUT_HANDLE = -11,
        STD_ERROR_HANDLE = -12,
    }

    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
    /// </remarks>
    internal enum ApplyFlags : long
    {
        /// <summary>Indicates no special handling.</summary>
        None = 0,

        /// <summary>Allow MSDelta to apply deltas created using PatchAPI.</summary>
        AllowLegacy = 1,
    }

    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#filetypesets
    /// </remarks>
    [Flags]
    internal enum FileTypeSet : long
    {
        /// <summary>
        ///     File type set that includes I386, IA64 and AMD64 Portable Executable (PE) files. Others are treated as raw.
        /// </summary>
        Executables = 0x0FL,
    }

    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltaflagtypeflags
    /// </remarks>
    internal enum CreateFlags : long
    {
        /// <summary>Indicates no special handling.</summary>
        None = 0,

        /// <summary>Allow the source, target and delta files to exceed the default size limit.</summary>
        IgnoreFileSizeLimit = 1 << 17,
    }

    /// <remarks>
    ///     http://msdn.microsoft.com/en-us/library/bb417345.aspx#deltainputstructure
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeltaInput
    {
        /// <summary>Memory address non-editable input buffer.</summary>
        public IntPtr Start;

        /// <summary>Size of the memory buffer in bytes.</summary>
        public IntPtr Size;

        /// <summary>
        ///     Defines whether MSDelta is allowed to edit the input buffer. If you make the input editable, the buffer will
        ///     be zeroed at function return. However this will cause most MSDelta functions to use less memory.
        /// </summary>
        [MarshalAs(UnmanagedType.Bool)] public bool Editable;
    }

    internal enum HashAlgId
    {
        /// <summary>No signature.</summary>
        None = 0,

        /// <summary>32-bit CRC defined in msdelta.dll.</summary>
        Crc32 = 32,
    }
}
