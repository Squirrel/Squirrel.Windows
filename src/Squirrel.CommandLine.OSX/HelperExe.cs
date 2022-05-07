using System;

namespace Squirrel.CommandLine
{
    internal class HelperExe : HelperFile
    {
        public static string UpdatePath 
            => FindHelperFile("UpdateMac", p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _));
    }
}
