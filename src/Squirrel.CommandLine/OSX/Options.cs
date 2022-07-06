using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NET.HostModel.AppHost;

namespace Squirrel.CommandLine.OSX
{
    internal class PackOptions : BaseOptions
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
        public bool noDelta { get; private set; }
        public string signAppIdentity { get; private set; }
        public string signInstallIdentity { get; private set; }
        public string signEntitlements { get; private set; }
        public string notaryProfile { get; private set; }
        public string appleId { get; private set; }
        public KeyValuePair<string, string>[] pkgContent => _pkgContent.ToArray();

        private Dictionary<string, string> _pkgContent = new Dictionary<string, string>();

        public PackOptions()
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
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
            Add("appleId", "Override the apple bundle ID for generated bundles", v => appleId = v);
            Add("noPkg", "Skip generating a .pkg installer", v => appleId = v);
            Add("pkgContent=", "Add content files (eg. readme, license) to pkg installer.", (v1, v2) => _pkgContent.Add(v1, v2));

            if (SquirrelRuntimeInfo.IsOSX) {
                Add("signAppIdentity=", "The {SUBJECT} name of the cert to use for app code signing", v => signAppIdentity = v);
                Add("signInstallIdentity=", "The {SUBJECT} name of the cert to use for installation packages", v => signInstallIdentity = v);
                Add("signEntitlements=", "{PATH} to entitlements file for hardened runtime", v => signEntitlements = v);
                Add("notaryProfile=", "{NAME} of profile containing Apple credentials stored with notarytool", v => notaryProfile = v);
            }
        }

        public override void Validate()
        {
            IsRequired(nameof(packId), nameof(packVersion), nameof(packDirectory));
            IsValidFile(nameof(signEntitlements), "entitlements");
            NuGet.NugetUtil.ThrowIfInvalidNugetId(packId);
            NuGet.NugetUtil.ThrowIfVersionNotSemverCompliant(packVersion);
            IsValidDirectory(nameof(packDirectory), true);

            var validContentKeys = new string[] {
                "welcome",
                "readme",
                "license",
                "conclusion",
            };

            foreach (var kvp in _pkgContent) {
                if (!validContentKeys.Contains(kvp.Key)) {
                    throw new OptionValidationException($"Invalid pkgContent key: {kvp.Key}. Must be one of: " + string.Join(", ", validContentKeys));
                }

                if (!File.Exists(kvp.Value)) {
                    throw new OptionValidationException("pkgContent file not found: " + kvp.Value);
                }
            }
        }
    }
}