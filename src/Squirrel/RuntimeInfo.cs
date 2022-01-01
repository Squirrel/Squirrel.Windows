using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary> Dotnet Runtime SKU </summary>
    public enum DotnetRuntimeType
    {
        /// <summary> The .NET Runtime contains just the components needed to run a console app </summary>
        DotNet = 1,
        /// <summary> The The ASP.NET Core Runtime enables you to run existing web/server applications </summary>
        AspNetCore,
        /// <summary> The .NET Desktop Runtime enables you to run existing Windows desktop applications </summary>
        WindowsDesktop,
        /// <summary> The .NET SDK enables you to compile dotnet applications you intend to run on other systems </summary>
        SDK,
    }

    /// <summary> The Runtime CPU Architecture </summary>
    public enum RuntimeCpu
    {
        /// <summary> Unknown / Unspecified </summary>
        Unknown = 0,
        /// <summary> Intel x86 </summary>
        X86 = 1,
        /// <summary> x64 / amd64 </summary>
        X64 = 2,
    }

    /// <summary> Runtime installation result code </summary>
    public enum RuntimeInstallResult
    {
        /// <summary> The install was successful </summary>
        Success = 0,
        /// <summary> The install failed because it was cancelled by the user </summary>
        UserCancelled = 1602,
        /// <summary> The install failed because another install is in progress, try again later </summary>
        AnotherInstallInProgress = 1618,
        /// <summary> The install failed because a system restart is required before continuing </summary>
        RestartRequired = 3010,
        /// <summary> The install failed because the current system does not support this runtime (outdated/unsupported) </summary>
        SystemDoesNotMeetRequirements = 5100,
    }

    /// <summary> Base type containing information about a runtime in relation to the current operating system </summary>
    public abstract class RuntimeInfo
    {
        /// <summary> The unique Id of this runtime. Can be used to retrieve a runtime instance with <see cref="Runtimes.GetRuntimeByName(string)"/> </summary>
        public string Id { get; }

        /// <summary> The human-friendly name of this runtime - for displaying to users </summary>
        public string DisplayName { get; }

        internal readonly static IFullLogger Log = SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(RuntimeInfo));

        /// <summary> Creates a new instance with the specified properties </summary>
        protected RuntimeInfo(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        /// <summary> Retrieves the web url to the latest compatible runtime installer exe </summary>
        public abstract Task<string> GetDownloadUrl();

        /// <summary> Check if a runtime compatible with the current instance is installed on this system </summary>
        public abstract Task<bool> CheckIsInstalled();

        /// <summary> Check if this runtime is supported on the current system </summary>
        public abstract Task<bool> CheckIsSupported();

        /// <summary> Download the latest installer for this runtime to the specified file </summary>
        public virtual async Task DownloadToFile(string localPath, Action<DownloadProgressChangedEventArgs> progress = null)
        {
            var url = await GetDownloadUrl().ConfigureAwait(false);
            Log.Info($"Downloading {Id} from {url} to {localPath}");
            using var wc = Utility.CreateWebClient();
            wc.DownloadProgressChanged += (s, e) => { progress?.Invoke(e); };
            await wc.DownloadFileTaskAsync(url, localPath).ConfigureAwait(false);
        }

        /// <summary> Execute a runtime installer at a local file path. Typically used after <see cref="DownloadToFile"/> </summary>
        public virtual async Task<RuntimeInstallResult> InvokeInstaller(string pathToInstaller, bool isQuiet)
        {
            var args = new string[] { "/passive", "/norestart", "/showrmui" };
            var quietArgs = new string[] { "/q", "/norestart" };
            Log.Info($"Running {Id} installer '{pathToInstaller} {string.Join(" ", args)}'");
            var p = await Utility.InvokeProcessAsync(pathToInstaller, isQuiet ? quietArgs : args, CancellationToken.None).ConfigureAwait(false);

            // https://johnkoerner.com/install/windows-installer-error-codes/

            if (p.ExitCode == 1638) // a newer compatible version is already installed
                return RuntimeInstallResult.Success;

            if (p.ExitCode == 1641) // installer initiated a restart
                return RuntimeInstallResult.RestartRequired;

            return (RuntimeInstallResult) p.ExitCode;
        }

        /// <summary> The unique string representation of this runtime </summary>
        public override string ToString() => $"[{Id}] {DisplayName}";

        /// <summary> The unique hash code of this runtime </summary>
        public override int GetHashCode() => Id.GetHashCode();
    }

    /// <summary> Represents a full .NET Framework runtime, usually included in Windows automatically through Windows Update </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public class FxRuntimeInfo : RuntimeInfo
    {
        /// <summary> Permalink to the runtime installer for this runtime </summary>
        public string DownloadUrl { get; }

        /// <summary> The minimum compatible release version for this runtime </summary>
        public int ReleaseVersion { get; }

        private const string ndpPath = "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";

        /// <inheritdoc/>
        public FxRuntimeInfo(string id, string displayName, string downloadUrl, int releaseVersion) : base(id, displayName)
        {
            DownloadUrl = downloadUrl;
            ReleaseVersion = releaseVersion;
        }

        /// <inheritdoc/>
        public override Task<string> GetDownloadUrl()
        {
            return Task.FromResult(DownloadUrl);
        }

        /// <inheritdoc/>
        public override Task<bool> CheckIsSupported()
        {
            // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine
            return Task.FromResult(true);
        }

        /// <inheritdoc/>
        public override Task<bool> CheckIsInstalled()
        {
            using var view = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using var key = view.OpenSubKey(ndpPath);
            if (key == null) return Task.FromResult(false);

            var dwRelease = key.GetValue("Release") as int?;
            if (dwRelease == null) return Task.FromResult(false);

            return Task.FromResult(dwRelease.Value >= ReleaseVersion);
        }
    }

    /// <summary> Represents a modern DOTNET runtime where versions are deployed independenly of the operating system </summary>
    public class DotnetRuntimeInfo : RuntimeInfo
    {
        /// <summary> A two part version (eg. '5.0') used to search for the latest current patch </summary>
        public string RequiredVersion { get; }

        /// <summary> The CPU architecture of the runtime. This must match the RID of the app being deployed.
        /// For example, if the Squirrel app was deployed with 'win-x64', this must be X64 also. </summary>
        public RuntimeCpu CpuArchitecture { get; }

        /// <inheritdoc/>
        public DotnetRuntimeInfo(string id, string displayName, string version, RuntimeCpu architecture) : base(id, displayName)
        {
            RequiredVersion = version;
            CpuArchitecture = architecture;
        }

        private const string UncachedDotNetFeed = "https://dotnetcli.blob.core.windows.net/dotnet";
        private const string DotNetFeed = "https://dotnetcli.azureedge.net/dotnet";

        /// <inheritdoc/>
        public override async Task<bool> CheckIsInstalled()
        {
            switch (CpuArchitecture) {

            case RuntimeCpu.X64: return await CheckIsInstalledX64().ConfigureAwait(false);
            case RuntimeCpu.X86: return CheckIsInstalledX86();
            default: return false;

            }
        }

        /// <inheritdoc/>
        public override Task<bool> CheckIsSupported()
        {
            if (CpuArchitecture == RuntimeCpu.X64 && !Environment.Is64BitOperatingSystem)
                return Task.FromResult(false);

            // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine
            return Task.FromResult(true);
        }

        private bool CheckIsInstalledX86()
        {
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return CheckIsInstalledInBaseDirectory(pf86);
        }

        private async Task<bool> CheckIsInstalledX64()
        {
            if (!Environment.Is64BitOperatingSystem)
                return false;

            // we are probably an x86 process, and I don't know of any great ways to
            // get the x64 ProgramFiles directory from an x86 process, so this code
            // is extremely unfortunate.

            if (Environment.Is64BitProcess) {
                // this only works in a 64 bit process, otherwise it points to ProgramFilesX86
                var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (CheckIsInstalledInBaseDirectory(pf64))
                    return true;
            }

            // https://docs.microsoft.com/en-us/windows/win32/winprog64/wow64-implementation-details
            var pf64compat = Environment.GetEnvironmentVariable("ProgramW6432");
            if (Directory.Exists(pf64compat))
                return CheckIsInstalledInBaseDirectory(pf64compat);

            // on a 64 bit operating system, the dotnet cli should be x64, and will only
            // return x64 results, so we can ask it as a last resort
            try {
                var token = new CancellationTokenSource(2000).Token;
                var output = await Utility.InvokeProcessAsync("dotnet", new[] { "--info" }, token).ConfigureAwait(false);
                if (output.ExitCode != 0) return false;
                return output.StdOutput.Contains("Microsoft.WindowsDesktop.App " + RequiredVersion);
            } catch (Win32Exception wex) when (wex.HResult == -2147467259) {
                return false; // executable not found
            }
        }

        private bool CheckIsInstalledInBaseDirectory(string baseDirectory)
        {
            var directory = Path.Combine(baseDirectory, "dotnet", "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(directory))
                return false;

            var myVer = new Version(RequiredVersion);

            var dirs = Directory.EnumerateDirectories(directory)
                .Select(d => Path.GetFileName(d))
                .Where(d => Version.TryParse(d, out var _))
                .Select(d => Version.Parse(d));

            return dirs.Any(v => v.Major == myVer.Major && v.Minor == myVer.Minor);
        }

        /// <inheritdoc/>
        public override async Task<string> GetDownloadUrl()
        {
            var latest = await GetLatestDotNetVersion(DotnetRuntimeType.WindowsDesktop, RequiredVersion).ConfigureAwait(false);
            var architecture = CpuArchitecture switch {
                RuntimeCpu.X86 => "x86",
                RuntimeCpu.X64 => "x64",
                _ => throw new ArgumentOutOfRangeException(nameof(CpuArchitecture)),
            };

            return GetDotNetDownloadUrl(DotnetRuntimeType.WindowsDesktop, latest, architecture);
        }

        /// <summary>
        /// Get latest available version of dotnet. Channel can be 'LTS', 'current', or a two part version 
        /// (eg. '6.0') to get the latest minor release.
        /// </summary>
        public static async Task<string> GetLatestDotNetVersion(DotnetRuntimeType runtimeType, string channel)
        {
            // https://github.com/dotnet/install-scripts/blob/main/src/dotnet-install.ps1#L427
            // these are case sensitive
            string runtime = runtimeType switch {
                DotnetRuntimeType.DotNet => "dotnet",
                DotnetRuntimeType.AspNetCore => "aspnetcore",
                DotnetRuntimeType.WindowsDesktop => "WindowsDesktop",
                DotnetRuntimeType.SDK => "Sdk",
                _ => throw new NotImplementedException(),
            };

            using var wc = Utility.CreateWebClient();
            return await wc.DownloadStringTaskAsync(new Uri($"{UncachedDotNetFeed}/{runtime}/{channel}/latest.version")).ConfigureAwait(false);
        }

        /// <summary>
        /// Get download url for a specific version of dotnet. Version must be an absolute version, such as one
        /// returned by <see cref="GetLatestDotNetVersion(DotnetRuntimeType, string)"/>. cpuarch should be either
        /// 'x86', 'x64', or 'arm64'.
        /// </summary>
        public static string GetDotNetDownloadUrl(DotnetRuntimeType runtimeType, string version, string cpuarch)
        {
            // https://github.com/dotnet/install-scripts/blob/main/src/dotnet-install.ps1#L619
            return runtimeType switch {
                DotnetRuntimeType.DotNet => $"{DotNetFeed}/Runtime/{version}/dotnet-runtime-{version}-win-{cpuarch}.exe",
                DotnetRuntimeType.AspNetCore => $"{DotNetFeed}/aspnetcore/Runtime/{version}/aspnetcore-runtime-{version}-win-{cpuarch}.exe",
                DotnetRuntimeType.WindowsDesktop =>
                    new Version(version).Major >= 5
                        ? $"{DotNetFeed}/WindowsDesktop/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe"
                        : $"{DotNetFeed}/Runtime/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe",
                DotnetRuntimeType.SDK => $"{DotNetFeed}/Sdk/{version}/dotnet-sdk-{version}-win-{cpuarch}.exe",
                _ => throw new NotImplementedException(),
            };
        }
    }

    /// <summary> Represents a VC++ 2015-2022 redistributable, to support native applications </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public class VCredistRuntimeInfo : RuntimeInfo
    {
        /// <summary> The minimum compatible version that must be installed </summary>
        public Version MinVersion { get; }

        /// <summary> The CPU architecture of the runtime </summary>
        public RuntimeCpu CpuArchitecture { get; }

        /// <inheritdoc/>
        public VCredistRuntimeInfo(string id, string displayName, Version minVersion, RuntimeCpu cpuArchitecture) : base(id, displayName)
        {
            MinVersion = minVersion;
            CpuArchitecture = cpuArchitecture;
        }

        /// <inheritdoc/>
        public override Task<bool> CheckIsInstalled()
        {
            return Task.FromResult(GetInstalledVCVersions().Any(
                v => v.Cpu == CpuArchitecture &&
                v.Ver.Major == MinVersion.Major &&
                v.Ver >= MinVersion));
        }

        /// <inheritdoc/>
        public override Task<bool> CheckIsSupported()
        {
            if (CpuArchitecture == RuntimeCpu.X64 && !Environment.Is64BitOperatingSystem)
                return Task.FromResult(false);

            // TODO use IsWindowsVersionOrGreater function to verify it can be installed on this machine
            return Task.FromResult(true);
        }

        const string UninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <inheritdoc/>
        public static (Version Ver, RuntimeCpu Cpu)[] GetInstalledVCVersions()
        {
            List<(Version Ver, RuntimeCpu Cpu)> results = new List<(Version Ver, RuntimeCpu Cpu)>();

            void searchreg(RegistryKey view)
            {
                foreach (var kn in view.GetSubKeyNames()) {
                    var subKey = view.OpenSubKey(kn);
                    var name = subKey.GetValue("DisplayName") as string;
                    if (name != null && name.Contains("Microsoft Visual C++") && name.Contains("Redistributable")) {
                        var version = subKey.GetValue("DisplayVersion") as string;
                        if (Version.TryParse(version, out var v)) {
                            // these entries do not get added into the correct registry hive, so we need to determine
                            // the cpu architecture from the name. I hate this but what can I do?
                            if (name.Contains("x64") && Environment.Is64BitOperatingSystem) {
                                results.Add((v, RuntimeCpu.X64));
                            } else {
                                results.Add((v, RuntimeCpu.X86));
                            }
                        }
                    }
                }
            }

            using var view86 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
               .CreateSubKey(UninstallRegSubKey, RegistryKeyPermissionCheck.ReadSubTree);
            searchreg(view86);

            if (Environment.Is64BitOperatingSystem) {
                using var view64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .CreateSubKey(UninstallRegSubKey, RegistryKeyPermissionCheck.ReadSubTree);
                searchreg(view64);
            }

            return results.OrderBy(v => v.Ver).ToArray();
        }

        /// <inheritdoc/>
        public override Task<string> GetDownloadUrl()
        {
            // https://docs.microsoft.com/en-US/cpp/windows/latest-supported-vc-redist?view=msvc-170#visual-studio-2015-2017-2019-and-2022
            // https://docs.microsoft.com/en-us/cpp/porting/binary-compat-2015-2017?view=msvc-170
            return Task.FromResult(CpuArchitecture switch {
                RuntimeCpu.X86 => "https://aka.ms/vs/17/release/vc_redist.x86.exe",
                RuntimeCpu.X64 => "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                _ => throw new ArgumentOutOfRangeException(nameof(CpuArchitecture)),
            });
        }
    }
}
