using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using NuGet.Versioning;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    /// <summary>
    /// Contains static properties to access common supported runtimes, and a function to search for a runtime by name
    /// </summary>
    public static partial class Runtimes
    {
        /// <summary> Runtime for .NET Framework 4.5 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK45 = new("net45", ".NET Framework 4.5", "http://go.microsoft.com/fwlink/?LinkId=397707", 378389);
        /// <summary> Runtime for .NET Framework 4.5.1 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK451 = new("net451", ".NET Framework 4.5.1", "http://go.microsoft.com/fwlink/?LinkId=397707", 378675);
        /// <summary> Runtime for .NET Framework 4.5.2 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK452 = new("net452", ".NET Framework 4.5.2", "http://go.microsoft.com/fwlink/?LinkId=397707", 379893);
        /// <summary> Runtime for .NET Framework 4.6 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK46 = new("net46", ".NET Framework 4.6", "http://go.microsoft.com/fwlink/?LinkId=780596", 393295);
        /// <summary> Runtime for .NET Framework 4.6.1 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK461 = new("net461", ".NET Framework 4.6.1", "http://go.microsoft.com/fwlink/?LinkId=780596", 394254);
        /// <summary> Runtime for .NET Framework 4.6.2 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK462 = new("net462", ".NET Framework 4.6.2", "http://go.microsoft.com/fwlink/?LinkId=780596", 394802);
        /// <summary> Runtime for .NET Framework 4.7 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK47 = new("net47", ".NET Framework 4.7", "http://go.microsoft.com/fwlink/?LinkId=863262", 460798);
        /// <summary> Runtime for .NET Framework 4.7.1 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK471 = new("net471", ".NET Framework 4.7.1", "http://go.microsoft.com/fwlink/?LinkId=863262", 461308);
        /// <summary> Runtime for .NET Framework 4.7.2 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK472 = new("net472", ".NET Framework 4.7.2", "http://go.microsoft.com/fwlink/?LinkId=863262", 461808);
        /// <summary> Runtime for .NET Framework 4.8 </summary>
        public static readonly FrameworkInfo NETFRAMEWORK48 = new("net48", ".NET Framework 4.8", "http://go.microsoft.com/fwlink/?LinkId=2085155", 528040);


        /// <summary> Runtime for .NET Core 3.1 Desktop Runtime (x86) </summary>
        public static readonly DotnetInfo DOTNETCORE31_X86 = new("3.1", RuntimeCpu.x86); // eg. netcoreapp3.1-x86
        /// <summary> Runtime for .NET Core 3.1 Desktop Runtime (x64) </summary>
        public static readonly DotnetInfo DOTNETCORE31_X64 = new("3.1", RuntimeCpu.x64); // eg. netcoreapp3.1-x64
        /// <summary> Runtime for .NET 5.0 Desktop Runtime (x86) </summary>
        public static readonly DotnetInfo DOTNET5_X86 = new("5.0", RuntimeCpu.x86); // eg. net5.0.14-x86
        /// <summary> Runtime for .NET 5.0 Desktop Runtime (x64) </summary>
        public static readonly DotnetInfo DOTNET5_X64 = new("5.0", RuntimeCpu.x64); // eg. net5
        /// <summary> Runtime for .NET 6.0 Desktop Runtime (x86) </summary>
        public static readonly DotnetInfo DOTNET6_X86 = new("6.0.2", RuntimeCpu.x86); // eg. net6-x86
        /// <summary> Runtime for .NET 6.0 Desktop Runtime (x64) </summary>
        public static readonly DotnetInfo DOTNET6_X64 = new("6.0.2", RuntimeCpu.x64); // eg. net6.0.2


        /// <summary> Runtime for Visual C++ 2010 Redistributable (x86) </summary>
        public static readonly VCRedist00 VCREDIST100_X86 = new("vcredist100-x86", "Visual C++ 2010 Redistributable (x86)", new(10, 00, 40219), RuntimeCpu.x86,
            "https://download.microsoft.com/download/C/6/D/C6D0FD4E-9E53-4897-9B91-836EBA2AACD3/vcredist_x86.exe");
        /// <summary> Runtime for Visual C++ 2010 Redistributable (x64) </summary>
        public static readonly VCRedist00 VCREDIST100_X64 = new("vcredist100-x64", "Visual C++ 2010 Redistributable (x64)", new(10, 00, 40219), RuntimeCpu.x64,
            "https://download.microsoft.com/download/A/8/0/A80747C3-41BD-45DF-B505-E9710D2744E0/vcredist_x64.exe");
        /// <summary> Runtime for Visual C++ 2012 Redistributable (x86) </summary>
        public static readonly VCRedist00 VCREDIST110_X86 = new("vcredist110-x86", "Visual C++ 2012 Redistributable (x86)", new(11, 00, 61030), RuntimeCpu.x86,
            "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe");
        /// <summary> Runtime for Visual C++ 2012 Redistributable (x64) </summary>
        public static readonly VCRedist00 VCREDIST110_X64 = new("vcredist110-x64", "Visual C++ 2012 Redistributable (x64)", new(11, 00, 61030), RuntimeCpu.x64,
            "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe");
        /// <summary> Runtime for Visual C++ 2013 Redistributable (x86) </summary>
        public static readonly VCRedist00 VCREDIST120_X86 = new("vcredist120-x86", "Visual C++ 2013 Redistributable (x86)", new(12, 00, 40664), RuntimeCpu.x86,
            "https://aka.ms/highdpimfc2013x86enu");
        /// <summary> Runtime for Visual C++ 2013 Redistributable (x64) </summary>
        public static readonly VCRedist00 VCREDIST120_X64 = new("vcredist120-x64", "Visual C++ 2013 Redistributable (x64)", new(12, 00, 40664), RuntimeCpu.x64,
            "https://aka.ms/highdpimfc2013x64enu");
        /// <summary> Runtime for Visual C++ 2015 Redistributable (x86) </summary>
        public static readonly VCRedist14 VCREDIST140_X86 = new("vcredist140-x86", "Visual C++ 2015 Redistributable (x86)", new(14, 00, 23506), RuntimeCpu.x86);
        /// <summary> Runtime for Visual C++ 2015 Redistributable (x64) </summary>
        public static readonly VCRedist14 VCREDIST140_X64 = new("vcredist140-x64", "Visual C++ 2015 Redistributable (x64)", new(14, 00, 23506), RuntimeCpu.x64);
        /// <summary> Runtime for Visual C++ 2017 Redistributable (x86) </summary>
        public static readonly VCRedist14 VCREDIST141_X86 = new("vcredist141-x86", "Visual C++ 2017 Redistributable (x86)", new(14, 15, 26706), RuntimeCpu.x86);
        /// <summary> Runtime for Visual C++ 2017 Redistributable (x64) </summary>
        public static readonly VCRedist14 VCREDIST141_X64 = new("vcredist141-x64", "Visual C++ 2017 Redistributable (x64)", new(14, 15, 26706), RuntimeCpu.x64);
        /// <summary> Runtime for Visual C++ 2019 Redistributable (x86) </summary>
        public static readonly VCRedist14 VCREDIST142_X86 = new("vcredist142-x86", "Visual C++ 2019 Redistributable (x86)", new(14, 20, 27508), RuntimeCpu.x86);
        /// <summary> Runtime for Visual C++ 2019 Redistributable (x64) </summary>
        public static readonly VCRedist14 VCREDIST142_X64 = new("vcredist142-x64", "Visual C++ 2019 Redistributable (x64)", new(14, 20, 27508), RuntimeCpu.x64);
        /// <summary> Runtime for Visual C++ 2022 Redistributable (x86) </summary>
        public static readonly VCRedist14 VCREDIST143_X86 = new("vcredist143-x86", "Visual C++ 2022 Redistributable (x86)", new(14, 30, 30704), RuntimeCpu.x86);
        /// <summary> Runtime for Visual C++ 2022 Redistributable (x64) </summary>
        public static readonly VCRedist14 VCREDIST143_X64 = new("vcredist143-x64", "Visual C++ 2022 Redistributable (x64)", new(14, 30, 30704), RuntimeCpu.x64);

        /// <summary> An array of all the currently supported runtimes </summary>
        public static readonly RuntimeInfo[] All;

        internal static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Runtimes));

        static Runtimes()
        {
            All = typeof(Runtimes)
                .GetFields()
                .Where(f => typeof(RuntimeInfo).IsAssignableFrom(f.FieldType))
                .Select(f => (RuntimeInfo) f.GetValue(null))
                .ToArray();
        }

        /// <summary> 
        /// Search for a runtime by name. If a platform architecture is not specified, the default is x64.
        /// Returns null if no match is found. 
        /// </summary>
        public static RuntimeInfo GetRuntimeByName(string name)
        {
            var rt = All.FirstOrDefault(r => r.Id.Equals(name, StringComparison.InvariantCulture))
                ?? All.FirstOrDefault(r => r.Id.Equals(name + "-x64", StringComparison.InvariantCulture));

            if (rt != null)
                return rt;

            if (DotnetInfo.TryParse(name, out var dn))
                return dn;

            return null;
        }

        /// <summary> Returns an array of runtimes representing the input string, or throws if the dependencies can not be parsed. </summary>
        public static IEnumerable<RuntimeInfo> ParseDependencyString(string dependencies)
        {
            List<RuntimeInfo> requiredFrameworks = new List<RuntimeInfo>();
            if (!String.IsNullOrWhiteSpace(dependencies)) {
                var frameworks = dependencies.Split(',').Select(d => d.Trim());
                foreach (var f in frameworks) {
                    var r = Runtimes.GetRuntimeByName(f);
                    if (r == null) {
                        throw new Exception($"Runtime '{f}' is unsupported. Specify a dotnet runtime (eg. 'net6.0') or a static runtime such as: {String.Join(", ", Runtimes.All.Select(r => r.Id))}");
                    }

                    if (r is DotnetInfo dni) {
                        // check if the provided dotnet version is actually valid and warn the user if not
                        string latest = null;
                        try {
                            latest = DotnetInfo.GetLatestDotNetVersion(DotnetRuntimeType.WindowsDesktop, $"{dni.MinVersion.Major}.{dni.MinVersion.Minor}")
                                .ConfigureAwait(false).GetAwaiter().GetResult();
                        } catch (Exception ex) {
                            Log.ErrorException($"Unable to verify version for runtime '{f}'", ex);
                        }

                        if (latest != null && NuGetVersion.Parse(latest) < dni.MinVersion) {
                            throw new Exception($"For runtime string '{f}', version provided ({dni.MinVersion}) is greater than the latest available version ({latest}).");
                        }
                    }

                    requiredFrameworks.Add(r);
                }
            }
            return requiredFrameworks;
        }
    }
}
