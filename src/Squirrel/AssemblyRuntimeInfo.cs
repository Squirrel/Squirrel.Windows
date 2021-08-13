using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Squirrel
{
    internal static class AssemblyRuntimeInfo
    {
        public static string EntryExePath { get; }
        public static string BaseDirectory { get; }
        public static AssemblyName ExecutingAssemblyName => Assembly.GetExecutingAssembly().GetName();

        static AssemblyRuntimeInfo()
        {
            EntryExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            BaseDirectory = AppContext.BaseDirectory;
        }
    }
}
