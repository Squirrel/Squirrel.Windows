using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Squirrel
{
    internal static class AssemblyRuntimeInfo
    {
        public static string EntryExePath { get; }
        public static string BaseDirectory { get; }
        public static AssemblyName ExecutingAssemblyName => Assembly.GetExecutingAssembly().GetName();
        public static bool IsSingleFile { get; }

        static AssemblyRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;

            var assyPath = Assembly.GetEntryAssembly().Location;
            if (String.IsNullOrEmpty(assyPath) || !File.Exists(assyPath))
                IsSingleFile = true;
        }
    }
}
