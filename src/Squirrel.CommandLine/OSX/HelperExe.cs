using System;
using System.Runtime.Versioning;

namespace Squirrel.CommandLine.OSX
{
    internal class HelperExe : HelperFile
    {
        public static string UpdateMacPath 
            => FindHelperFile("UpdateMac", p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _));
    }
}
