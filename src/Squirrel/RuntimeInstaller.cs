using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Squirrel
{
    public abstract class RuntimeInfo
    {
        public string Id { get; }
        public string DisplayName { get; }

        protected RuntimeInfo(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public abstract Task<string> GetDownloadUrl();

        public abstract Task<bool> CheckIsInstalled();
    }

    public class FxRuntimeInfo : RuntimeInfo
    {
        public string DownloadUrl { get; }
        public int ReleaseVersion { get; }

        private const string ndpPath = "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full";

        public FxRuntimeInfo(string id, string displayName, string downloadUrl, int releaseVersion) : base(id, displayName)
        {
            DownloadUrl = downloadUrl;
            ReleaseVersion = releaseVersion;
        }

        public override Task<string> GetDownloadUrl()
        {
            return Task.FromResult(DownloadUrl);
        }

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

    public class DotNetRuntimeInfo : RuntimeInfo
    {
        public string Version { get; }

        public DotNetRuntimeInfo(string id, string displayName, string version) : base(id, displayName)
        {
            Version = version;
        }

        private const string UncachedDotNetFeed = "https://dotnetcli.blob.core.windows.net/dotnet";
        private const string DotNetFeed = "https://dotnetcli.azureedge.net/dotnet";

        public override async Task<bool> CheckIsInstalled()
        {
            // it is possible to parse this registry entry, but it only returns the newest version
            // and it might be necessary to install an older version of the runtime if it's not installed,
            // so we need a full list of installed runtimes. 
            // static const wchar_t* dncPath = L"SOFTWARE\\dotnet\\Setup\\InstalledVersions";

            // note, dotnet cli will only return x64 results.
            //auto runtimes = exec("dotnet --list-runtimes");

            try {
                var token = new CancellationTokenSource(2000).Token;
                var output = await Utility.InvokeProcessAsync("dotnet", new[] { "--info" }, token);
                if (output.ExitCode != 0) return false;
                return output.StdOutput.Contains("Microsoft.WindowsDesktop.App " + Version);
            } catch (Win32Exception wex) when (wex.HResult == -2147467259) {
                return false; // executable not found
            }
        }

        public override async Task<string> GetDownloadUrl()
        {
            var latest = await GetLatestDotNetVersion(RuntimeType.WindowsDesktop, Version);
            return GetDotNetDownloadUrl(RuntimeType.WindowsDesktop, latest, "x64"); // todo, cpu arch configurable
        }

        public enum RuntimeType
        {
            DotNet = 1,
            AspNetCore,
            WindowsDesktop,
            SDK,
        }

        /// <summary>
        /// Get latest available version of dotnet. Channel can be 'LTS', 'current', or a two part version 
        /// (eg. '6.0') to get the latest minor release.
        /// </summary>
        public static async Task<string> GetLatestDotNetVersion(RuntimeType runtimeType, string channel)
        {
            // these are case sensitive
            string runtime = runtimeType switch {
                RuntimeType.DotNet => "dotnet",
                RuntimeType.AspNetCore => "aspnetcore",
                RuntimeType.WindowsDesktop => "WindowsDesktop",
                RuntimeType.SDK => "Sdk",
                _ => throw new NotImplementedException(),
            };

            using var wc = Utility.CreateWebClient();
            return await wc.DownloadStringTaskAsync(new Uri($"{UncachedDotNetFeed}/{runtime}/{channel}/latest.version"));
        }

        /// <summary>
        /// Get download url for a specific version of dotnet. Version must be an absolute version, such as one
        /// returned by <see cref="GetLatestDotNetVersion(RuntimeType, string)"/>. cpuarch should be either
        /// 'x86', 'x64', or 'arm64'.
        /// </summary>
        public static string GetDotNetDownloadUrl(RuntimeType runtimeType, string version, string cpuarch)
        {
            return runtimeType switch {
                RuntimeType.DotNet => $"{DotNetFeed}/Runtime/{version}/dotnet-runtime-{version}-win-{cpuarch}.exe",
                RuntimeType.AspNetCore => $"{DotNetFeed}/aspnetcore/Runtime/{version}/aspnetcore-runtime-{version}-win-{cpuarch}.exe",
                RuntimeType.WindowsDesktop =>
                    new Version(version).Major >= 5
                        ? $"{DotNetFeed}/WindowsDesktop/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe"
                        : $"{DotNetFeed}/Runtime/{version}/windowsdesktop-runtime-{version}-win-{cpuarch}.exe",
                RuntimeType.SDK => $"{DotNetFeed}/Sdk/{version}/dotnet-sdk-{version}-win-{cpuarch}.exe",
                _ => throw new NotImplementedException(),
            };
        }
    }

    public static class RuntimeInstaller
    {
        public static readonly FxRuntimeInfo NET45 = new("net45", ".NET Framework 4.5", "http://go.microsoft.com/fwlink/?LinkId=397707", 378389);
        public static readonly FxRuntimeInfo NET451 = new("net451", ".NET Framework 4.5.1", "http://go.microsoft.com/fwlink/?LinkId=397707", 378675);
        public static readonly FxRuntimeInfo NET452 = new("net452", ".NET Framework 4.5.2", "http://go.microsoft.com/fwlink/?LinkId=397707", 379893);
        public static readonly FxRuntimeInfo NET46 = new("net46", ".NET Framework 4.6", "http://go.microsoft.com/fwlink/?LinkId=780596", 393295);
        public static readonly FxRuntimeInfo NET461 = new("net461", ".NET Framework 4.6.1", "http://go.microsoft.com/fwlink/?LinkId=780596", 394254);
        public static readonly FxRuntimeInfo NET462 = new("net462", ".NET Framework 4.6.2", "http://go.microsoft.com/fwlink/?LinkId=780596", 394802);
        public static readonly FxRuntimeInfo NET47 = new("net47", ".NET Framework 4.7", "http://go.microsoft.com/fwlink/?LinkId=863262", 460798);
        public static readonly FxRuntimeInfo NET471 = new("net471", ".NET Framework 4.7.1", "http://go.microsoft.com/fwlink/?LinkId=863262", 461308);
        public static readonly FxRuntimeInfo NET472 = new("net472", ".NET Framework 4.7.2", "http://go.microsoft.com/fwlink/?LinkId=863262", 461808);
        public static readonly FxRuntimeInfo NET48 = new("net48", ".NET Framework 4.8", "http://go.microsoft.com/fwlink/?LinkId=2085155", 528040);
        public static readonly DotNetRuntimeInfo DOTNETCORE31 = new("netcoreapp31", ".NET Core 3.1", "3.1");
        public static readonly DotNetRuntimeInfo DOTNET5 = new("net5", ".NET 5", "5.0");
        public static readonly DotNetRuntimeInfo DOTNET6 = new("net6", ".NET 6", "6.0");

        public static readonly RuntimeInfo[] All = new RuntimeInfo[] {
            NET45, NET451, NET452,
            NET46, NET461, NET462,
            NET47, NET471, NET472,
            NET48,
            DOTNETCORE31, DOTNET5, DOTNET6
        };

        public static RuntimeInfo GetRuntimeInfoByName(string name)
        {
            return All.FirstOrDefault(r => r.Id.Equals(name, StringComparison.InvariantCulture));
        }

        public static async Task<int> InvokeInstaller(string pathToInstaller, bool isQuiet)
        {
            var args = new string[] { "/passive", "/norestart", "/showrmui" };
            var quietArgs = new string[] { "/q", "/norestart" };
            var p = await Utility.InvokeProcessAsync(pathToInstaller, isQuiet ? quietArgs : args, CancellationToken.None);
            return p.ExitCode;
        }
    }
}
