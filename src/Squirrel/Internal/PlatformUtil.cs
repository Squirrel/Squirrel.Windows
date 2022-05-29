using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    internal static class PlatformUtil
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(PlatformUtil));

        private const string OSX_CSTD_LIB = "libSystem.dylib";
        private const string NIX_CSTD_LIB = "libc";
        private const string WIN_KERNEL32 = "kernel32.dll";
        private const string WIN_SHELL32 = "shell32.dll";
        private const string WIN_NTDLL = "NTDLL.DLL";
        private const string WIN_PSAPI = "psapi.dll";

        [SupportedOSPlatform("linux")]
        [DllImport(NIX_CSTD_LIB, EntryPoint = "getppid")]
        private static extern int nix_getppid();

        [SupportedOSPlatform("osx")]
        [DllImport(OSX_CSTD_LIB, EntryPoint = "getppid")]
        private static extern int osx_getppid();

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32)]
        private static extern IntPtr GetCurrentProcess();

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_NTDLL, SetLastError = true)]
        private static extern int NtQueryInformationProcess(IntPtr hProcess, int pic, ref PROCESS_BASIC_INFORMATION pbi, int cb, out int pSize);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public nint ExitStatus;
            public nint PebBaseAddress;
            public nint AffinityMask;
            public nint BasePriority;
            public nuint UniqueProcessId;
            public nint InheritedFromUniqueProcessId;
        }

        public static Process GetParentProcess()
        {
            int parentId;

            if (SquirrelRuntimeInfo.IsWindows) {
                var pbi = new PROCESS_BASIC_INFORMATION();
                NtQueryInformationProcess(GetCurrentProcess(), 0, ref pbi, Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)), out _);
                parentId = (int) pbi.InheritedFromUniqueProcessId;
            } else if (SquirrelRuntimeInfo.IsLinux) {
                parentId = nix_getppid();
            } else if (SquirrelRuntimeInfo.IsOSX) {
                parentId = osx_getppid();
            } else {
                throw new PlatformNotSupportedException();
            }

            // the parent process has exited (nix/osx)
            if (parentId <= 1)
                return null;

            try {
                var p = Process.GetProcessById(parentId);

                // the retrieved process is not our parent, the pid has been reused
                if (p.StartTime > Process.GetCurrentProcess().StartTime)
                    return null;

                return p;
            } catch (ArgumentException) {
                // the process has exited (windows)
                return null;
            }
        }

        public static void WaitForParentProcessToExit()
        {
            var p = GetParentProcess();
            if (p == null) {
                Log.Warn("Will not wait. Parent process has already exited.");
                return;
            }

            Log.Info($"Waiting for PID {p.Id} to exit (60s timeout)...");
            var exited = p.WaitForExit(60_000);
            if (!exited) {
                throw new Exception("Parent wait timed out.");
            }

            Log.Info($"PID {p.Id} has exited.");
        }

        [SupportedOSPlatform("osx")]
        [DllImport(OSX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
        private static extern int osx_chmod(string pathname, int mode);

        [SupportedOSPlatform("linux")]
        [DllImport(NIX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
        private static extern int nix_chmod(string pathname, int mode);

        public static void ChmodFileAsExecutable(string filePath)
        {
            Func<string, int, int> chmod;

            if (SquirrelRuntimeInfo.IsOSX) chmod = osx_chmod;
            else if (SquirrelRuntimeInfo.IsLinux) chmod = nix_chmod;
            else return; // no-op on windows, all .exe files can be executed.

            var filePermissionOctal = Convert.ToInt32("777", 8);
            const int EINTR = 4;
            int chmodReturnCode;

            do {
                chmodReturnCode = chmod(filePath, filePermissionOctal);
            } while (chmodReturnCode == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (chmodReturnCode == -1) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set file permission {filePermissionOctal} for {filePath}.");
            }
        }

        private enum MagicMachO : uint
        {
            MH_MAGIC = 0xfeedface,
            MH_CIGAM = 0xcefaedfe,
            MH_MAGIC_64 = 0xfeedfacf,
            MH_CIGAM_64 = 0xcffaedfe
        }

        public static bool IsMachOImage(string filePath)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filePath))) {
                if (reader.BaseStream.Length < 256) // Header size
                    return false;

                uint magic = reader.ReadUInt32();
                return Enum.IsDefined(typeof(MagicMachO), magic);
            }
        }

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_PSAPI, SetLastError = true)]
        private static extern bool EnumProcesses(
            IntPtr pProcessIds, // pointer to allocated DWORD array
            int cb,
            out int pBytesReturned);

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            [In] int justPassZeroHere,
            [Out] StringBuilder lpImageFileName,
            [In] [MarshalAs(UnmanagedType.U4)] ref int nSize);

        [Flags]
        private enum ProcessAccess : uint
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
        
        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, SetLastError = true)]
        private static extern IntPtr OpenProcess(
            ProcessAccess processAccess,
            bool bInheritHandle,
            int processId);

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hHandle);

        [SupportedOSPlatform("windows")]
        private static List<(string ProcessExePath, int ProcessId)> GetRunningProcessesWindows()
        {
            var pids = new int[2048];
            var gch = GCHandle.Alloc(pids, GCHandleType.Pinned);
            try {
                if (!EnumProcesses(gch.AddrOfPinnedObject(), sizeof(int) * pids.Length, out var bytesReturned))
                    throw new Win32Exception("Failed to enumerate processes");

                if (bytesReturned < 1)
                    throw new Exception("Failed to enumerate processes");

                List<(string ProcessExePath, int ProcessId)> ret = new();

                for (int i = 0; i < bytesReturned / sizeof(int); i++) {
                    IntPtr hProcess = IntPtr.Zero;
                    try {
                        hProcess = OpenProcess(ProcessAccess.QueryLimitedInformation, false, pids[i]);
                        if (hProcess == IntPtr.Zero)
                            continue;

                        var sb = new StringBuilder(256);
                        var capacity = sb.Capacity;
                        if (!QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                            continue;

                        var exePath = sb.ToString();
                        if (String.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                            continue;

                        ret.Add((sb.ToString(), pids[i]));
                    } catch (Exception) {
                        // don't care
                    } finally {
                        if (hProcess != IntPtr.Zero)
                            CloseHandle(hProcess);
                    }
                }

                return ret;
            } finally {
                gch.Free();
            }
        }

        public static List<(string ProcessExePath, int ProcessId)> GetRunningProcesses()
        {
            IEnumerable<(string ProcessExePath, int ProcessId)> processes = SquirrelRuntimeInfo.IsWindows
                ? GetRunningProcessesWindows()
                : Process.GetProcesses().Select(p => (p.MainModule?.FileName, p.Id));

            return processes
                .Where(x => !String.IsNullOrWhiteSpace(x.ProcessExePath)) // Processes we can't query will have an empty process name
                .ToList();
        }

        public static List<(string ProcessExePath, int ProcessId)> GetRunningProcessesInDirectory(string directory)
        {
            return GetRunningProcesses()
                .Where(x => Utility.IsFileInDirectory(x.ProcessExePath, directory))
                .ToList();
        }

        public static void KillProcessesInDirectory(string directoryToKill)
        {
            Log.Info("Killing all processes in " + directoryToKill);
            var myPid = Process.GetCurrentProcess().Id;
            int c = 0;
            foreach (var x in GetRunningProcessesInDirectory(directoryToKill)) {
                if (myPid == x.ProcessId) {
                    Log.Info($"Skipping '{x.ProcessExePath}' (is current process)");
                    continue;
                }

                try {
                    Process.GetProcessById(x.ProcessId).Kill();
                    c++;
                } catch (Exception ex) {
                    Log.WarnException($"Unable to terminate process (pid.{x.ProcessId})", ex);
                }
            }

            Log.Info($"Terminated {c} processes successfully.");
        }
        
        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, EntryPoint = "LocalFree", SetLastError = true)]
        private static extern IntPtr _LocalFree(IntPtr hMem);

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_SHELL32, EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
        private static extern IntPtr _CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmdLine, out int numArgs);

        [SupportedOSPlatform("windows")]
        public static string[] CommandLineToArgvW(string cmdLine)
        {
            IntPtr argv = IntPtr.Zero;
            try {
                argv = _CommandLineToArgvW(cmdLine, out var numArgs);
                if (argv == IntPtr.Zero) {
                    throw new Win32Exception();
                }
                var result = new string[numArgs];

                for (int i = 0; i < numArgs; i++) {
                    IntPtr currArg = Marshal.ReadIntPtr(argv, i * Marshal.SizeOf(typeof(IntPtr)));
                    result[i] = Marshal.PtrToStringUni(currArg);
                }

                return result;
            } finally {
                _LocalFree(argv);
            }
        }
        
        /*
         * caesay — 09/12/2021 at 12:10 PM
         * yeah
         * can I steal this for squirrel? 
         * Roman — 09/12/2021 at 12:10 PM
         * sure :)
         * reference CommandRunner.cs on the github url as source? :)
         * https://github.com/RT-Projects/RT.Util/blob/ef660cd693f66bc946da3aaa368893b03b74eed7/RT.Util.Core/CommandRunner.cs#L327
         */

        /// <summary>
        ///     Given a number of argument strings, constructs a single command line string with all the arguments escaped
        ///     correctly so that a process using standard Windows API for parsing the command line will receive exactly the
        ///     strings passed in here. See Remarks.</summary>
        /// <remarks>
        ///     The string is only valid for passing directly to a process. If the target process is invoked by passing the
        ///     process name + arguments to cmd.exe then further escaping is required, to counteract cmd.exe's interpretation
        ///     of additional special characters. See <see cref="EscapeCmdExeMetachars"/>.</remarks>
        [SupportedOSPlatform("windows")]
        private static string ArgsToCommandLine(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args) {
                if (arg == null)
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');
                // For details, see https://web.archive.org/web/20150318010344/http://blogs.msdn.com/b/twistylittlepassagesallalike/archive/2011/04/23/everyone-quotes-arguments-the-wrong-way.aspx
                // or https://devblogs.microsoft.com/oldnewthing/?p=12833
                if (arg.Length != 0 && arg.IndexOfAny(_cmdChars) < 0)
                    sb.Append(arg);
                else {
                    sb.Append('"');
                    for (int c = 0; c < arg.Length; c++) {
                        int backslashes = 0;
                        while (c < arg.Length && arg[c] == '\\') {
                            c++;
                            backslashes++;
                        }
                        if (c == arg.Length) {
                            sb.Append('\\', backslashes * 2);
                            break;
                        } else if (arg[c] == '"') {
                            sb.Append('\\', backslashes * 2 + 1);
                            sb.Append('"');
                        } else {
                            sb.Append('\\', backslashes);
                            sb.Append(arg[c]);
                        }
                    }
                    sb.Append('"');
                }
            }
            return sb.ToString();
        }
        private static readonly char[] _cmdChars = new[] { ' ', '"', '\n', '\t', '\v' };

        /// <summary>
        ///     Escapes all cmd.exe meta-characters by prefixing them with a ^. See <see cref="ArgsToCommandLine"/> for more
        ///     information.</summary>
        [SupportedOSPlatform("windows")]
        private static string EscapeCmdExeMetachars(string command)
        {
            var result = new StringBuilder();
            foreach (var ch in command) {
                switch (ch) {
                case '(':
                case ')':
                case '%':
                case '!':
                case '^':
                case '"':
                case '<':
                case '>':
                case '&':
                case '|':
                    result.Append('^');
                    break;
                }
                result.Append(ch);
            }
            return result.ToString();
        }

        private static string ArgsToCommandLineUnix(IEnumerable<string> args)
        {
            var sb = new StringBuilder();
            foreach (var arg in args) {
                if (arg == null)
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');

                // there are just too many 'command chars' in unix, so we play it 
                // super safe here and escape the string if there are any non-alpha-numeric
                if (System.Text.RegularExpressions.Regex.IsMatch(arg, @"$[\w]^")) {
                    sb.Append(arg);
                } else {
                    // https://stackoverflow.com/a/33949338/184746
                    // single quotes are 'strong quotes' and can contain everything
                    // except never other single quotes.
                    sb.Append("'");
                    sb.Append(arg.Replace("'", @"'\''"));
                    sb.Append("'");
                }
            }
            return sb.ToString();
        }

        private static ProcessStartInfo CreateProcessStartInfo(string fileName, string workingDirectory)
        {
            var psi = new ProcessStartInfo(fileName);
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.ErrorDialog = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
            return psi;
        }

        private static (ProcessStartInfo StartInfo, string CommandDisplayString) CreateProcessStartInfo(string fileName, IEnumerable<string> args, string workingDirectory)
        {
            var psi = CreateProcessStartInfo(fileName, workingDirectory);

            string displayArgs;

#if NET5_0_OR_GREATER
            foreach (var a in args) psi.ArgumentList.Add(a);
            displayArgs = $"['{String.Join("', '", args)}']";
#else
            psi.Arguments = displayArgs = SquirrelRuntimeInfo.IsWindows ? ArgsToCommandLine(args) : ArgsToCommandLineUnix(args);
#endif

            return (psi, fileName + " " + displayArgs);
        }

        private static (int ExitCode, string StdOutput) InvokeProcess(ProcessStartInfo psi, CancellationToken ct)
        {
            var pi = Process.Start(psi);
            while (!ct.IsCancellationRequested) {
                if (pi.WaitForExit(500)) break;
            }

            if (ct.IsCancellationRequested && !pi.HasExited) {
                pi.Kill();
                ct.ThrowIfCancellationRequested();
            }

            string output = pi.StandardOutput.ReadToEnd();
            string error = pi.StandardError.ReadToEnd();
            var all = (output ?? "") + Environment.NewLine + (error ?? "");

            return (pi.ExitCode, all.Trim());
        }

        public static Process StartProcessNonBlocking(string fileName, IEnumerable<string> args, string workingDirectory)
        {
            var (psi, cmd) = CreateProcessStartInfo(fileName, args, workingDirectory);
            return Process.Start(psi);
        }
        
        public static Process StartProcessNonBlocking(string fileName, string args, string workingDirectory)
        {
            var psi = CreateProcessStartInfo(fileName, workingDirectory);
            psi.Arguments = args;
            return Process.Start(psi);
        }

        public static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct)
        {
            var (psi, cmd) = CreateProcessStartInfo(fileName, args, workingDirectory);
            var p = InvokeProcess(psi, ct);
            return (p.ExitCode, p.StdOutput, cmd);
        }

        //public static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, string args, string workingDirectory, CancellationToken ct)
        //{
        //    var psi = CreateProcessStartInfo(fileName, workingDirectory);
        //    psi.Arguments = args;
        //    var p = InvokeProcess(psi, ct);
        //    return (p.ExitCode, p.StdOutput, fileName + " " + args);
        //}

        public static Task<(int ExitCode, string StdOutput, string Command)> InvokeProcessAsync(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct)
        {
            return Task.Run(() => InvokeProcess(fileName, args, workingDirectory, ct));
        }

        private enum StandardHandles : int
        {
            STD_INPUT_HANDLE = -10,
            STD_OUTPUT_HANDLE = -11,
            STD_ERROR_HANDLE = -12,
        }
        
        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, EntryPoint = "GetStdHandle")]
        private static extern IntPtr GetStdHandle(StandardHandles nStdHandle);

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32, EntryPoint = "AllocConsole")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [SupportedOSPlatform("windows")]
        [DllImport(WIN_KERNEL32)]
        private static extern bool AttachConsole(int pid);
        
        static int consoleCreated = 0;
        
        [SupportedOSPlatform("windows")]
        public static void AttachConsole()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;

            if (Interlocked.CompareExchange(ref consoleCreated, 1, 0) == 1) return;

            if (!AttachConsole(-1)) {
                AllocConsole();
            }

            GetStdHandle(StandardHandles.STD_ERROR_HANDLE);
            GetStdHandle(StandardHandles.STD_OUTPUT_HANDLE);
        }
    }
}