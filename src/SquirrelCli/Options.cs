using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Lib;

namespace SquirrelCli
{
    internal abstract class BaseOptions : ValidatedOptionSet
    {
        public string releaseDir { get; private set; } = ".\\Releases";
        public BaseOptions()
        {
            Add("r=|releaseDir=", "Output directory for releasified packages", v => releaseDir = v);
        }
    }

    internal class ReleasifyOptions : BaseOptions
    {
        public string package { get; set; }
        public string baseUrl { get; private set; }
        public string signParams { get; private set; }
        public string framework { get; private set; }
        public string splashImage { get; private set; }
        public string updateIcon { get; private set; }
        public string appIcon { get; private set; }
        public string setupIcon { get; private set; }
        public bool noDelta { get; private set; }
        public bool allowUnaware { get; private set; }
        public string msi { get; private set; }

        public ReleasifyOptions()
        {
            // hidden arguments
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("allowUnaware", "Allows building packages without a SquirrelAwareApp (disabled by default)", v => allowUnaware = true, true);
            Add("addSearchPath=", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc", v => HelperExe.AddSearchPath(v), true);

            // public arguments
            Add("p=|package=", "Path to a '.nupkg' package to releasify", v => package = v);
            Add("n=|signParams=", "Sign files via SignTool.exe using these parameters", v => signParams = v);
            Add("f=|framework=", "List of required runtimes to install during setup -\nexample: 'net6,vcredist143'", v => framework = v);
            Add("s=|splashImage=", "Splash image to be displayed during installation", v => splashImage = v);
            Add("i=|icon=", "Sets all the icons: Update, App, and Setup",
                (v) => { updateIcon = v; appIcon = v; setupIcon = v; });
            Add("updateIcon=", ".ico to be used for Update.exe", v => updateIcon = v);
            Add("appIcon=", ".ico to be used in the 'Apps and Features' list", v => appIcon = v);
            Add("setupIcon=", ".ico to be used for Setup.exe", v => setupIcon = v);
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
            Add("msi=", "Compiles a .msi machine-wide deployment tool.\nThis value must be either 'x86' 'x64'", v => msi = v.ToLower());
        }

        public override void Validate()
        {
            ValidateInternal(true);
        }

        protected virtual void ValidateInternal(bool checkPackage)
        {
            IsValidFile(nameof(appIcon), ".ico");
            IsValidFile(nameof(setupIcon), ".ico");
            IsValidFile(nameof(updateIcon), ".ico");
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));

            if (checkPackage) {
                IsRequired(nameof(package));
                IsValidFile(nameof(package), ".nupkg");
            }

            if (!String.IsNullOrEmpty(msi))
                if (!msi.Equals("x86") && !msi.Equals("x64"))
                    throw new OptionValidationException($"Argument 'msi': File must be either 'x86' or 'x64'. Actual value was '{msi}'.");
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
            Add("packName=", "The name of the package to create", v => packId = v, true);

            // public arguments, with indexes so they appear before ReleasifyOptions
            InsertAt(1, "u=|packId=", "Unique identifier for application/package", v => packId = v);
            InsertAt(2, "v=|packVersion=", "Current application version", v => packVersion = v);
            InsertAt(3, "p=|packDirectory=", "Directory containing application files to package", v => packDirectory = v);
            InsertAt(4, "packTitle=", "Optional display/friendly name for package", v => packTitle = v);
            InsertAt(5, "packAuthors=", "Optional company or list of package authors", v => packAuthors = v);
            InsertAt(6, "includePdb", "Include *.pdb files in the package", v => includePdb = true);
            InsertAt(7, "releaseNotes=", "File containing markdown notes for this version", v => releaseNotes = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(packId), nameof(packVersion), nameof(packDirectory));
            Squirrel.NuGet.NugetUtil.ThrowIfInvalidNugetId(packId);
            Squirrel.NuGet.NugetUtil.ThrowIfVersionNotSemverCompliant(packVersion);
            IsValidDirectory(nameof(packDirectory), true);
            IsValidFile(nameof(releaseNotes));
            base.ValidateInternal(false);
        }
    }

    internal class SyncBackblazeOptions : BaseOptions
    {
        public string b2KeyId { get; private set; }
        public string b2AppKey { get; private set; }
        public string b2BucketId { get; private set; }

        public SyncBackblazeOptions()
        {
            Add("b2BucketId=", v => b2BucketId = v);
            Add("b2keyid=", v => b2KeyId = v);
            Add("b2key=", v => b2AppKey = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(b2KeyId), nameof(b2AppKey), nameof(b2BucketId));
        }
    }

    internal class SyncHttpOptions : BaseOptions
    {
        public string url { get; private set; }
        public string token { get; private set; }

        public SyncHttpOptions()
        {
            Add("url=", "Base url to the http location with hosted releases", v => url = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(url));
            IsValidUrl(nameof(url));
        }
    }

    internal class SyncGithubOptions : BaseOptions
    {
        public string repoUrl { get; private set; }
        public string token { get; private set; }

        public SyncGithubOptions()
        {
            Add("repoUrl=", "Full url to the github repository -\nexample: 'https://github.com/myname/myrepo'", v => repoUrl = v);
            Add("token=", "The oauth token to use as login credentials", v => token = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(repoUrl));
            IsValidUrl(nameof(repoUrl));
        }
    }
}
