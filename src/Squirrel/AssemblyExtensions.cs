using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Squirrel
{
    public static class AssemblyExtensions
    {
        public static void Restart(this Assembly assembly, string[] arguments = null)
        {
            ProcessStart(assembly, assembly.Location, arguments);
        }

        public static void ProcessStart(this Assembly assembly, string exeName, string[] arguments = null)
        {
            Process.Start(getProcessStartInfo(assembly, exeName, arguments));
        public static Process ProcessStart(this Assembly assembly, ProcessStartInfo psi)
        {
            psi.FileName = getUpdateExe(assembly);
            if (!string.IsNullOrEmpty(psi.Arguments))
            {
                psi.Arguments = string.Format("--process-start-args=\"{0}\"", string.Join(" ", psi.Arguments));
            }
            return Process.Start(psi);
        }

        public static ProcessStartInfo ProcessStartGetInfo(this Assembly assembly, string exeName, string[] arguments = null)
        {
            return getProcessStartInfo(assembly, exeName, arguments);
        }

        static ProcessStartInfo getProcessStartInfo(Assembly assembly, string exeName, string[] arguments)
        {
            var psi = new List<string>
            {
                string.Format("--process-start=\"{0}\"", exeName)
            };
            if (arguments != null && arguments.Length > 0)
            {
                psi.Add(string.Format("--process-start-args=\"{0}\"", string.Join(" ", arguments)));
            }
            return new ProcessStartInfo(getUpdateExe(assembly), string.Join(" ", psi));
        }

        static string getUpdateExe(Assembly assembly)
        {
            var rootAppDir = Path.Combine(Path.GetDirectoryName(assembly.Location), "..\\Update.exe");
            return rootAppDir;
        }
    }
}
