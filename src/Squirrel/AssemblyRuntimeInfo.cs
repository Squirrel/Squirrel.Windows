using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Squirrel
{
    public static class AssemblyRuntimeInfo
    {
        public static string EntryExePath { get; }
        public static string BaseDirectory { get; }
        public static AssemblyName ExecutingAssemblyName => Assembly.GetExecutingAssembly().GetName();
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
