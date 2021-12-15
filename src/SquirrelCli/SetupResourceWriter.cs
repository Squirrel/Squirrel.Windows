using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NET.HostModel;

namespace SquirrelCli
{
    internal class SetupResourceWriter
    {
        // these values come from Setup.rc / resource.h
        private static readonly ushort RESOURCE_LANG = 0x0409;
        private static readonly IntPtr IDR_UPDATE_ZIP = new IntPtr(131);
        private static readonly IntPtr IDR_FX_VERSION_FLAG = new IntPtr(132);
        private static readonly IntPtr IDR_SPLASH_IMG = new IntPtr(138);
        private static readonly IntPtr IDR_PACKAGE_NAME = new IntPtr(139);

        public static void WriteZipToSetup(string targetSetupExe, string zipFile, string targetFramework, string splashImage)
        {
            try {
                using var writer = new ResourceUpdater(targetSetupExe);

                var zipBytes = File.ReadAllBytes(zipFile);
                writer.AddResource(zipBytes, "DATA", IDR_UPDATE_ZIP, RESOURCE_LANG);

                var packageNameBytes = Encoding.Unicode.GetBytes(String.Concat(Path.GetFileName(zipFile), "\0\0"));
                writer.AddResource(packageNameBytes, "FLAGS", IDR_PACKAGE_NAME, RESOURCE_LANG);

                if (!String.IsNullOrWhiteSpace(targetFramework)) {
                    var stringBytes = Encoding.Unicode.GetBytes(String.Concat(targetFramework, "\0\0"));
                    writer.AddResource(stringBytes, "FLAGS", IDR_FX_VERSION_FLAG, RESOURCE_LANG);
                }

                if (!String.IsNullOrWhiteSpace(splashImage)) {
                    var splashBytes = File.ReadAllBytes(splashImage);
                    writer.AddResource(splashBytes, "DATA", IDR_SPLASH_IMG, RESOURCE_LANG);
                } else {
                    // the template Setup.exe has a built-in splash image used for testing. we need to remove it
                    //writer.ClearResource("DATA", IDR_SPLASH_IMG, RESOURCE_LANG);
                }

                writer.Update();
            } catch (HResultException hr) {
                throw new Win32Exception(hr.Win32HResult);
            }
        }

        public static void CopyStubExecutableResources(string peToCopy, string targetStubExecutable)
        {
            try {
                using var writer = new ResourceUpdater(targetStubExecutable, true);
                writer.AddResourcesFromPEImage(peToCopy);
                writer.Update();
            } catch (HResultException hr) {
                throw new Win32Exception(hr.Win32HResult);
            }
        }
    }
}
