using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using Squirrel.Lib;

namespace Squirrel.CommandLine
{
    internal class SigningOptions : BaseOptions
    {
        public string signParams { get; private set; }
        public string signTemplate { get; private set; }

        public SigningOptions()
        {
            Add("n=|signParams=", "Sign files via SignTool.exe using these {PARAMETERS}",
                v => signParams = v);
            Add("signTemplate=", "Use a custom signing {COMMAND}. '{{{{file}}}}' will be replaced by the path of the file to sign.",
                v => signTemplate = v);
        }

        public void SignPEFile(string filePath)
        {
            if (!String.IsNullOrEmpty(signParams)) {
                HelperExe.SignPEFilesWithSignTool(filePath, signParams);
            } else if (!String.IsNullOrEmpty(signTemplate)) {
                HelperExe.SignPEFilesWithTemplate(filePath, signTemplate);
            } else {
                Log.Debug($"No signing paramaters, file will not be signed: '{filePath}'.");
            }
        }

        public override void Validate()
        {
            if (!String.IsNullOrEmpty(signParams) && !String.IsNullOrEmpty(signTemplate)) {
                throw new OptionValidationException($"Cannot use 'signParams' and 'signTemplate' options together, please choose one or the other.");
            }

            if (!String.IsNullOrEmpty(signTemplate) && !signTemplate.Contains("{{file}}")) {
                throw new OptionValidationException($"Argument 'signTemplate': Must contain '{{{{file}}}}' in template string (replaced with the file to sign). Current value is '{signTemplate}'");
            }
        }
    }

    internal class ReleasifyOptions : SigningOptions
    {
        public string package { get; set; }
        public string baseUrl { get; private set; }
        public string framework { get; private set; }
        public string splashImage { get; private set; }
        public string icon { get; private set; }
        public string appIcon { get; private set; }
        public bool noDelta { get; private set; }
        public bool allowUnaware { get; private set; }
        public string msi { get; private set; }
        public string debugSetupExe { get; private set; }

        public ReleasifyOptions()
        {
            // hidden arguments
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("allowUnaware", "Allows building packages without a SquirrelAwareApp (disabled by default)", v => allowUnaware = true, true);
            Add("addSearchPath=", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc",
                v => HelperExe.AddSearchPath(v), true);
            Add("debugSetupExe=", "Uses the Setup.exe at this {PATH} to create the bundle, and then replaces it with the bundle. " +
                "Used for locally debugging Setup.exe with a real bundle attached.", v => debugSetupExe = v, true);

            // public arguments
            InsertAt(1, "p=|package=", "{PATH} to a '.nupkg' package to releasify", v => package = v);
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
            Add("f=|framework=", "List of required {RUNTIMES} to install during setup\nexample: 'net6,vcredist143'", v => framework = v);
            Add("s=|splashImage=", "{PATH} to image/gif displayed during installation", v => splashImage = v);
            Add("i=|icon=", "{PATH} to .ico for Setup.exe and Update.exe", v => icon = v);
            Add("appIcon=", "{PATH} to .ico for 'Apps and Features' list", v => appIcon = v);
            Add("msi=", "Compile a .msi machine-wide deployment tool with the specified {BITNESS}. (either 'x86' or 'x64')", v => msi = v.ToLower());
        }

        public override void Validate()
        {
            ValidateInternal(true);
        }

        protected virtual void ValidateInternal(bool checkPackage)
        {
            IsValidFile(nameof(appIcon), ".ico");
            IsValidFile(nameof(icon), ".ico");
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));

            if (checkPackage) {
                IsRequired(nameof(package));
                IsValidFile(nameof(package), ".nupkg");
            }

            if (!String.IsNullOrEmpty(msi))
                if (!msi.Equals("x86") && !msi.Equals("x64"))
                    throw new OptionValidationException($"Argument 'msi': File must be either 'x86' or 'x64'. Actual value was '{msi}'.");

            base.Validate();
        }
    }

    internal class PackOptions : ReleasifyOptions
    {
        public string packId { get; private set; }
        public string packTitle { get; private set; }
        public string packVersion { get; private set; }
        public string packAuthors { get; private set; }
        public string packDirectory { get; private set; }
        public bool includePdb { get; private set; }
        public string releaseNotes { get; private set; }

        public PackOptions()
        {
            // remove 'package' argument from ReleasifyOptions
            Remove("package");
            Remove("p");

            // hidden arguments
            Add("packName=", "The name of the package to create",
                v => { packId = v; Log.Warn("--packName is deprecated. Use --packId instead."); }, true);
            Add("packDirectory=", "", v => packDirectory = v, true);

            // public arguments, with indexes so they appear before ReleasifyOptions
            InsertAt(1, "u=|packId=", "Unique {ID} for release", v => packId = v);
            InsertAt(2, "v=|packVersion=", "Current {VERSION} for release", v => packVersion = v);
            InsertAt(3, "p=|packDir=", "{DIRECTORY} containing application files for release", v => packDirectory = v);
            InsertAt(4, "packTitle=", "Optional display/friendly {NAME} for release", v => packTitle = v);
            InsertAt(5, "packAuthors=", "Optional company or list of release {AUTHORS}", v => packAuthors = v);
            InsertAt(6, "includePdb", "Add *.pdb files to release package", v => includePdb = true);
            InsertAt(7, "releaseNotes=", "{PATH} to file with markdown notes for version", v => releaseNotes = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(packId), nameof(packVersion), nameof(packDirectory));
            Squirrel.NuGet.NugetUtil.ThrowIfInvalidNugetId(packId);
            Squirrel.NuGet.NugetUtil.ThrowIfVersionNotSemverCompliant(packVersion, true);
            IsValidDirectory(nameof(packDirectory), true);
            IsValidFile(nameof(releaseNotes));
            base.ValidateInternal(false);
        }
    }
}
