using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Squirrel.SimpleSplat;

#if !NETFRAMEWORK

using InteropArchitecture = System.Runtime.InteropServices.Architecture;

#endif

#if !NET6_0_OR_GREATER

namespace System.Runtime.Versioning
{
    internal class SupportedOSPlatformGuardAttribute : Attribute
    {
        public SupportedOSPlatformGuardAttribute(string platformName) { }
    }
}

#endif

#if NETFRAMEWORK || NETSTANDARD2_0_OR_GREATER

namespace System.Runtime.Versioning
{
    internal class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

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
        x64 = 0x8664,
        /// <summary> Arm64 </summary>
        arm64 = 0xAA64,
    }

    /// <summary>
    /// Convenience class which provides runtime information about the current executing process, 
    /// in a way that is safe in older and newer versions of the framework.
    /// </summary>
    public static class SquirrelRuntimeInfo
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

        /// <summary> True if executing on a Windows platform. </summary>
        [SupportedOSPlatformGuard("windows")]
        public static bool IsWindows => SystemOsName == "windows";

        /// <summary> True if executing on a Linux platform. </summary>
        [SupportedOSPlatformGuard("linux")]
        public static bool IsLinux => SystemOsName == "linux";

        /// <summary> True if executing on a MacOS / OSX platform. </summary>
        [SupportedOSPlatformGuard("osx")]
        public static bool IsOSX => SystemOsName == "osx";

        public static StringComparer PathStringComparer =>
            IsWindows ? StringComparer.InvariantCultureIgnoreCase : StringComparer.InvariantCulture;

        public static StringComparison PathStringComparison =>
            IsWindows ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

        private static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(SquirrelRuntimeInfo));

        static SquirrelRuntimeInfo()
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

        /// <summary>
        /// Given a list of machine architectures, this function will try to select the best 
        /// architecture for a Squirrel package to maximize compatibility.
        /// </summary>
        public static RuntimeCpu SelectPackageArchitecture(IEnumerable<RuntimeCpu> peparsed)
        {
            var pearchs = peparsed
                .Where(m => m != RuntimeCpu.Unknown)
                .Distinct()
                .ToArray();

            if (pearchs.Length > 1) {
                Log.Warn(
                    "Multiple squirrel aware binaries were detected with different machine architectures. " +
                    "This could result in the application failing to install or run.");
            }

            // CS: the first will be selected for the "package" architecture. this order is important,
            // because of emulation support. Arm64 generally supports x86/x64 emulation, and x64
            // often supports x86 emulation, so we want to pick the least compatible architecture
            // for the package.
            var archOrder = new[] { RuntimeCpu.arm64, RuntimeCpu.x64, RuntimeCpu.x86 };

            var pkg = archOrder.FirstOrDefault(o => pearchs.Contains(o));
            if (pkg == RuntimeCpu.arm64) {
                Log.Warn("arm64 support in Squirrel has not been tested and may have bugs.");
            }

            return pkg;
        }

        /// <summary>
        /// Checks a given package architecture against the current executing OS to detect
        /// if it can be properly installed and run.
        /// </summary>
        public static bool? IsPackageCompatibleWithCurrentOS(RuntimeCpu architecture)
        {
            if (SystemArchitecture == RuntimeCpu.Unknown || architecture == RuntimeCpu.Unknown)
                return null;

            if (IsWindows) {
                if (SystemArchitecture == RuntimeCpu.arm64) {
                    // x86 can be virtualized on windows arm64
                    if (architecture == RuntimeCpu.arm64) return true;
                    if (architecture == RuntimeCpu.x86) return true;
                    // x64 virtualisation is only avaliable on windows 11
                    // https://stackoverflow.com/questions/69038560/detect-windows-11-with-net-framework-or-windows-api
                    if (architecture == RuntimeCpu.x64 && Environment.OSVersion.Version.Build >= 22000) return true;
                }
                if (SystemArchitecture == RuntimeCpu.x64) {
                    if (architecture == RuntimeCpu.x64) return true;
                    if (architecture == RuntimeCpu.x86) return true;
                }
                if (SystemArchitecture == RuntimeCpu.x86) {
                    if (architecture == RuntimeCpu.x86) return true;
                }
            } else {
                throw new NotImplementedException("This check currently only supports Windows.");
            }

            return false;
        }

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
                    SystemArchitecture = RuntimeCpu.x64;
                    break;
                }
            }

            if (SystemArchitecture != RuntimeCpu.Unknown) {
                return;
            }

#if NETFRAMEWORK
            SystemArchitecture = Environment.Is64BitOperatingSystem ? RuntimeCpu.x64 : RuntimeCpu.x86;
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
                InteropArchitecture.X64 => RuntimeCpu.x64,
                InteropArchitecture.Arm64 => RuntimeCpu.arm64,
                _ => RuntimeCpu.Unknown,
            };
        }
#endif
    }
}
