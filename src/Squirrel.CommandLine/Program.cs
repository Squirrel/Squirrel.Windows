using System;

namespace Squirrel.CommandLine
{
    public class Program
    {
        public static int Main(string[] args)
        {
            if (SquirrelRuntimeInfo.IsWindows)
                return Windows.Program.MainWindows(args);

            if (SquirrelRuntimeInfo.IsOSX)
                return OSX.Program.MainOSX(args);

            throw new NotSupportedException("Unsupported OS: " + SquirrelRuntimeInfo.SystemOsName);
        }
    }
}