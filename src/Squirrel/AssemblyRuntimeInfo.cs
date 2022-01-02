using System;
using System.IO;
using System.Reflection;

namespace Squirrel
{
    /// <summary>
    /// Convenience class which provides runtime information about the current executing process, 
    /// in a way that is safe in older and newer versions of the framework, including SingleFileBundles
    /// </summary>
    public static class AssemblyRuntimeInfo
    {
        /// <summary> The path on disk of the entry assembly </summary>
        public static string EntryExePath { get; }
        /// <summary> Gets the directory that the assembly resolver uses to probe for assemblies. </summary>
        public static string BaseDirectory { get; }
        /// <summary> The name of the currently executing assembly </summary>
        public static AssemblyName ExecutingAssemblyName => Assembly.GetExecutingAssembly().GetName();
        /// <summary> Check if the current application is a published SingleFileBundle </summary>
        public static bool IsSingleFile { get; }

        static AssemblyRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;

            // if Assembly.Location does not exist, we're almost certainly bundled into a dotnet SingleFile
            // is there a better way to check for this?
            var assyPath = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location;
            if (String.IsNullOrEmpty(assyPath) || !File.Exists(assyPath))
                IsSingleFile = true;
        }
    }
}
