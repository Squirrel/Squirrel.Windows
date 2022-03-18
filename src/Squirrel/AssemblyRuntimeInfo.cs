using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

#if !NETFRAMEWORK
using InteropArchitecture = System.Runtime.InteropServices.Architecture;
#endif

namespace Squirrel
{
    // constants from winnt.h
    /// <summary> The Runtime CPU Architecture </summary>
    public enum RuntimeCpu : ushort
    {
        /// <summary> Unknown or unsupported </summary>
        Unknown = 0,
        /// <summary> Intel x86 </summary>
        x86 = 0x014c,
        /// <summary> x64 / Amd64 </summary>
        amd64 = 0x8664,
        /// <summary> Arm64 </summary>
        arm64 = 0xAA64,
    }

    /// <summary>
    /// Convenience class which provides runtime information about the current executing process, 
    /// in a way that is safe in older and newer versions of the framework.
    /// </summary>
    public static class AssemblyRuntimeInfo
    {
        /// <summary> The path on disk of the entry assembly. </summary>
        public static string EntryExePath { get; }

        /// <summary> Gets the directory that the assembly resolver uses to probe for assemblies. </summary>
        public static string BaseDirectory { get; }

        /// <summary> The name of the currently executing assembly. </summary>
        public static AssemblyName ExecutingAssemblyName => Assembly.GetExecutingAssembly().GetName();

        /// <summary> Check if the current application is a published SingleFileBundle. </summary>
        public static bool IsSingleFile { get; }

        /// <summary> The current machine architecture, ignoring the current process / pe architecture. </summary>
        public static RuntimeCpu SystemArchitecture { get; private set; }

        /// <summary> The name of the current OS - eg. 'windows', 'linux', or 'osx'. </summary>
        public static string SystemOsName { get; private set; }

        static AssemblyRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;

            // if Assembly.Location does not exist, we're almost certainly bundled into a dotnet SingleFile
            // TODO: there is a better way to check this - we can scan the currently executing binary for a
            // SingleFile bundle marker.
            var assyPath = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location;
            if (String.IsNullOrEmpty(assyPath) || !File.Exists(assyPath))
                IsSingleFile = true;

#if NETFRAMEWORK
            CheckArchitectureWindows();
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                CheckArchitectureWindows();
            } else {
                CheckArchitectureOther();
            }
#endif
        }

        [DllImport("kernel32", EntryPoint = "IsWow64Process2", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr hProcess, out ushort pProcessMachine, out ushort pNativeMachine);

        [DllImport("kernel32")]
        private static extern IntPtr GetCurrentProcess();

        private static void CheckArchitectureWindows()
        {
            SystemOsName = "windows";

            // find the actual OS architecture. We can't rely on the framework alone for this on Windows
            // because Wow64 virtualisation is good enough to trick us to believing we're running natively
            // in some cases unless we use functions that are not virtualized (such as IsWow64Process2)

            try {
                if (IsWow64Process2(GetCurrentProcess(), out var _, out var nativeMachine)) {
                    if (Utility.TryParseEnumU16<RuntimeCpu>(nativeMachine, out var val)) {
                        SystemArchitecture = val;
                    }
                }
            } catch {
                // don't care if this function is missing
            }

            if (SystemArchitecture != RuntimeCpu.Unknown) {
                return;
            }

            // https://docs.microsoft.com/en-gb/windows/win32/winprog64/wow64-implementation-details?redirectedfrom=MSDN
            var pf64compat =
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            if (!String.IsNullOrEmpty(pf64compat)) {
                switch (pf64compat) {
                case "ARM64":
                    SystemArchitecture = RuntimeCpu.arm64;
                    break;
                case "AMD64":
                    SystemArchitecture = RuntimeCpu.amd64;
                    break;
                }
            }

            if (SystemArchitecture != RuntimeCpu.Unknown) {
                return;
            }

#if NETFRAMEWORK
            SystemArchitecture = Environment.Is64BitOperatingSystem ? RuntimeCpu.amd64 : RuntimeCpu.x86;
#else
            CheckArchitectureOther();
#endif
        }

#if !NETFRAMEWORK
        private static void CheckArchitectureOther()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                SystemOsName = "windows";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                SystemOsName = "linux";
            } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                SystemOsName = "osx";
            }

            SystemArchitecture = RuntimeInformation.OSArchitecture switch {
                InteropArchitecture.X86 => RuntimeCpu.x86,
                InteropArchitecture.X64 => RuntimeCpu.amd64,
                InteropArchitecture.Arm64 => RuntimeCpu.arm64,
                _ => RuntimeCpu.Unknown,
            };
        }
#endif
    }
}
