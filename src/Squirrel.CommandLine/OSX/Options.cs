using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NET.HostModel.AppHost;

namespace Squirrel.CommandLine.OSX
{
    internal class BundleOptions : ValidatedOptionSet
    {
        public string packId { get; private set; }
        public string packTitle { get; private set; }
        public string packVersion { get; private set; }
        public string packDirectory { get; private set; }
        public string outputDirectory { get; private set; }
        public string icon { get; private set; }
        public string exeName { get; private set; }

        public BundleOptions()
        {
            Add("o=|outputDir=", "The {DIRECTORY} to create the bundle", v => outputDirectory = v);
            Add("e=|exeName=", "The file name of the main executable", v => exeName = v);
            Add("u=|packId=", "Unique {ID} for bundle", v => packId = v);
            Add("v=|packVersion=", "Current {VERSION} for bundle", v => packVersion = v);
            Add("p=|packDir=", "{DIRECTORY} containing application files for bundle", v => packDirectory = v);
            Add("packTitle=", "Optional display/friendly {NAME} for bundle", v => packTitle = v);
            Add("i=|icon=", "{PATH} to the .icns file for this bundle", v => icon = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(packId), nameof(packVersion), nameof(packDirectory));
            Squirrel.NuGet.NugetUtil.ThrowIfInvalidNugetId(packId);
            Squirrel.NuGet.NugetUtil.ThrowIfVersionNotSemverCompliant(packVersion, false);
            IsValidDirectory(nameof(packDirectory), true);

            IsRequired(nameof(icon));
            IsValidFile(nameof(icon), ".icns");

            if (exeName == null)
                exeName = packId;

            var exe = Path.Combine(packDirectory, exeName);
            if (!File.Exists(exe) || !MachOUtils.IsMachOImage(exe))
                throw new OptionValidationException($"Could not find mach-o executable at '{exe}'.");
        }
    }

    internal class PackOptions : BaseOptions
    {
        public string package { get; set; }
        public bool noDelta { get; private set; }
        public string baseUrl { get; private set; }
        public bool includePdb { get; private set; }
        public string releaseNotes { get; private set; }

        public PackOptions()
        {
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("p=|package=", "{PATH} to a '.app' directory to releasify", v => package = v);
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
            Add("includePdb", "Add *.pdb files to bundle", v => includePdb = true);
            Add("releaseNotes=", "{PATH} to file with markdown notes for version", v => releaseNotes = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(package));
            IsValidDirectory(nameof(package), true);
            if (!Utility.PathPartEndsWith(package, ".app"))
                throw new OptionValidationException("-p argument must point to a macos bundle directory ending in '.app'.");
        }
    }
}
