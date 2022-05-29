using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NET.HostModel.AppHost;

namespace Squirrel.CommandLine.OSX
{
    internal class BundleOptions : BaseOptions
    {
        public string packId { get; private set; }
        public string packTitle { get; private set; }
        public string packVersion { get; private set; }
        public string packAuthors { get; private set; }
        public string packDirectory { get; private set; }
        public bool includePdb { get; private set; }
        public string releaseNotes { get; private set; }
        public string icon { get; private set; }
        public string mainExe { get; private set; }

        public BundleOptions()
        {
            Add("u=|packId=", "Unique {ID} for bundle", v => packId = v);
            Add("v=|packVersion=", "Current {VERSION} for bundle", v => packVersion = v);
            Add("p=|packDir=", "{DIRECTORY} containing application files to bundle, or a " +
                               "directory ending in '.app' to convert to a release.", v => packDirectory = v);
            Add("packAuthors=", "Optional company or list of release {AUTHORS}", v => packAuthors = v);
            Add("packTitle=", "Optional display/friendly {NAME} for bundle", v => packTitle = v);
            Add("includePdb", "Add *.pdb files to release package", v => includePdb = true);
            Add("releaseNotes=", "{PATH} to file with markdown notes for version", v => releaseNotes = v);
            Add("e=|mainExe=", "The file {NAME} of the main executable", v => mainExe = v);
            Add("i=|icon=", "{PATH} to the .icns file for this bundle", v => icon = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(packId), nameof(packVersion), nameof(packDirectory));
            NuGet.NugetUtil.ThrowIfInvalidNugetId(packId);
            NuGet.NugetUtil.ThrowIfVersionNotSemverCompliant(packVersion, false);
            IsValidDirectory(nameof(packDirectory), true);
        }
    }

    internal class PackOptions : BaseOptions
    {
        public string package { get; set; }
        public bool noDelta { get; private set; }
        // public string baseUrl { get; private set; }

        public PackOptions()
        {
            // Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("p=|package=", "{PATH} to a '.app' directory to releasify", v => package = v);
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
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