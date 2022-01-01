using System;
using System.Linq;

namespace Squirrel
{
    /// <summary>
    /// Contains static properties to access common supported runtimes, and a function to search for a runtime by name
    /// </summary>
#if NET5_0_OR_GREATER
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
    public static class Runtimes
    {
        /// <summary> Runtime for .NET Framework 4.5 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK45 = new("net45", ".NET Framework 4.5", "http://go.microsoft.com/fwlink/?LinkId=397707", 378389);
        /// <summary> Runtime for .NET Framework 4.5.1 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK451 = new("net451", ".NET Framework 4.5.1", "http://go.microsoft.com/fwlink/?LinkId=397707", 378675);
        /// <summary> Runtime for .NET Framework 4.5.2 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK452 = new("net452", ".NET Framework 4.5.2", "http://go.microsoft.com/fwlink/?LinkId=397707", 379893);
        /// <summary> Runtime for .NET Framework 4.6 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK46 = new("net46", ".NET Framework 4.6", "http://go.microsoft.com/fwlink/?LinkId=780596", 393295);
        /// <summary> Runtime for .NET Framework 4.6.1 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK461 = new("net461", ".NET Framework 4.6.1", "http://go.microsoft.com/fwlink/?LinkId=780596", 394254);
        /// <summary> Runtime for .NET Framework 4.6.2 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK462 = new("net462", ".NET Framework 4.6.2", "http://go.microsoft.com/fwlink/?LinkId=780596", 394802);
        /// <summary> Runtime for .NET Framework 4.7 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK47 = new("net47", ".NET Framework 4.7", "http://go.microsoft.com/fwlink/?LinkId=863262", 460798);
        /// <summary> Runtime for .NET Framework 4.7.1 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK471 = new("net471", ".NET Framework 4.7.1", "http://go.microsoft.com/fwlink/?LinkId=863262", 461308);
        /// <summary> Runtime for .NET Framework 4.7.2 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK472 = new("net472", ".NET Framework 4.7.2", "http://go.microsoft.com/fwlink/?LinkId=863262", 461808);
        /// <summary> Runtime for .NET Framework 4.8 </summary>
        public static readonly FxRuntimeInfo NETFRAMEWORK48 = new("net48", ".NET Framework 4.8", "http://go.microsoft.com/fwlink/?LinkId=2085155", 528040);


        /// <summary> Runtime for .NET Core 3.1 Desktop Runtime (x86) </summary>
        public static readonly DotnetRuntimeInfo DOTNETCORE31_X86 = new("netcoreapp31-x86", ".NET Core 3.1 Desktop Runtime (x86)", "3.1", RuntimeCpu.X86);
        /// <summary> Runtime for .NET Core 3.1 Desktop Runtime (x64) </summary>
        public static readonly DotnetRuntimeInfo DOTNETCORE31_X64 = new("netcoreapp31-x64", ".NET Core 3.1 Desktop Runtime (x64)", "3.1", RuntimeCpu.X64);
        /// <summary> Runtime for .NET 5.0 Desktop Runtime (x86) </summary>
        public static readonly DotnetRuntimeInfo DOTNET5_X86 = new("net5-x86", ".NET 5.0 Desktop Runtime (x86)", "5.0", RuntimeCpu.X86);
        /// <summary> Runtime for .NET 5.0 Desktop Runtime (x64) </summary>
        public static readonly DotnetRuntimeInfo DOTNET5_X64 = new("net5-x64", ".NET 5.0 Desktop Runtime (x64)", "5.0", RuntimeCpu.X64);
        /// <summary> Runtime for .NET 6.0 Desktop Runtime (x86) </summary>
        public static readonly DotnetRuntimeInfo DOTNET6_X86 = new("net6-x86", ".NET 6.0 Desktop Runtime (x86)", "6.0", RuntimeCpu.X86);
        /// <summary> Runtime for .NET 6.0 Desktop Runtime (x64) </summary>
        public static readonly DotnetRuntimeInfo DOTNET6_X64 = new("net6-x64", ".NET 6.0 Desktop Runtime (x64)", "6.0", RuntimeCpu.X64);


        /// <summary> Runtime for Visual C++ 2015 Redistributable (x86) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST140_X86 = new("vcredist140-x86", "Visual C++ 2015 Redistributable (x86)", new(14, 00, 23506), RuntimeCpu.X86);
        /// <summary> Runtime for Visual C++ 2015 Redistributable (x64) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST140_X64 = new("vcredist140-x64", "Visual C++ 2015 Redistributable (x64)", new(14, 00, 23506), RuntimeCpu.X64);
        /// <summary> Runtime for Visual C++ 2017 Redistributable (x86) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST141_X86 = new("vcredist141-x86", "Visual C++ 2017 Redistributable (x86)", new(14, 15, 26706), RuntimeCpu.X86);
        /// <summary> Runtime for Visual C++ 2017 Redistributable (x64) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST141_X64 = new("vcredist141-x64", "Visual C++ 2017 Redistributable (x64)", new(14, 15, 26706), RuntimeCpu.X64);
        /// <summary> Runtime for Visual C++ 2019 Redistributable (x86) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST142_X86 = new("vcredist142-x86", "Visual C++ 2019 Redistributable (x86)", new(14, 20, 27508), RuntimeCpu.X86);
        /// <summary> Runtime for Visual C++ 2019 Redistributable (x64) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST142_X64 = new("vcredist142-x64", "Visual C++ 2019 Redistributable (x64)", new(14, 20, 27508), RuntimeCpu.X64);
        /// <summary> Runtime for Visual C++ 2022 Redistributable (x86) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST143_X86 = new("vcredist143-x86", "Visual C++ 2022 Redistributable (x86)", new(14, 30, 30704), RuntimeCpu.X86);
        /// <summary> Runtime for Visual C++ 2022 Redistributable (x64) </summary>
        public static readonly VCredistRuntimeInfo VCREDIST143_X64 = new("vcredist143-x64", "Visual C++ 2022 Redistributable (x64)", new(14, 30, 30704), RuntimeCpu.X64);

        /// <summary> An array of all the currently supported runtimes </summary>
        public static readonly RuntimeInfo[] All;

        static Runtimes()
        {
            All = typeof(Runtimes)
                .GetFields()
                .Where(f => typeof(RuntimeInfo).IsAssignableFrom(f.FieldType))
                .Select(f => (RuntimeInfo) f.GetValue(null))
                .ToArray();
        }

        /// <summary> Search for a runtime by name. If a platform architecture is not specified, the default is x64 </summary>
        public static RuntimeInfo GetRuntimeByName(string name)
        {
            return All.FirstOrDefault(r => r.Id.Equals(name, StringComparison.InvariantCulture))
                ?? All.FirstOrDefault(r => r.Id.Equals(name + "-x64", StringComparison.InvariantCulture));
        }
    }
}
