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
        X86 = 0x014c,
        /// <summary> x64 / Amd64 </summary>
        X64 = 0x8664,
        // <summary> Arm64 </summary>
        // Arm64 = 0xAA64,
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

        /// <summary> The machine architecture, regardless of the current process / binary architecture. </summary>
        public static RuntimeCpu Architecture { get; private set; }

        static AssemblyRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;

            // if Assembly.Location does not exist, we're almost certainly bundled into a dotnet SingleFile
            // is there a better way to check for this?
            var assyPath = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location;
            if (String.IsNullOrEmpty(assyPath) || !File.Exists(assyPath))
                IsSingleFile = true;

#if !NETFRAMEWORK
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
#endif
                CheckArchitectureWindows();
#if !NETFRAMEWORK
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
            // find the actual OS architecture. We can't rely on the framework alone for this on Windows
            // because Wow64 virtualisation is good enough to trick us to believing we're running natively
            // in some cases unless we use functions that are not virtualized (such as IsWow64Process2)

            try {
                if (IsWow64Process2(GetCurrentProcess(), out var _, out var nativeMachine)) {
                    if (Utility.TryParseEnumU16<RuntimeCpu>(nativeMachine, out var val)) {
                        Architecture = val;
                    }
                }
            } catch {
                // don't care if this function is missing
            }

            if (Architecture != RuntimeCpu.Unknown) {
                return;
            }

            // https://docs.microsoft.com/en-gb/windows/win32/winprog64/wow64-implementation-details?redirectedfrom=MSDN
            var pf64compat =
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");

            if (!String.IsNullOrEmpty(pf64compat)) {
                switch (pf64compat) {
                //case "ARM64":
                //    Architecture = RuntimeCpu.Arm64;
                //    break;
                case "AMD64":
                    Architecture = RuntimeCpu.X64;
                    break;
                }
            }

            if (Architecture != RuntimeCpu.Unknown) {
                return;
            }

#if NETFRAMEWORK
            Architecture = Environment.Is64BitOperatingSystem ? RuntimeCpu.X64 : RuntimeCpu.X86;
#else
            CheckArchitectureOther();
#endif
        }

#if !NETFRAMEWORK
        private static void CheckArchitectureOther()
        {
            Architecture = RuntimeInformation.OSArchitecture switch {
                InteropArchitecture.X86 => RuntimeCpu.X86,
                InteropArchitecture.X64 => RuntimeCpu.X64,
                //InteropArchitecture.Arm64 => RuntimeCpu.Arm64,
                _ => RuntimeCpu.Unknown,
            };
        }
#endif
    }
}
