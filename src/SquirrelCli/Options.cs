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
            Add("r=|releaseDir=", "Release directory containing releasified packages", v => releaseDir = v);
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
        public string setupIcon { get; private set; }
        public string setupName { get; private set; } = "Setup";
        public bool noDelta { get; private set; }

        public ReleasifyOptions()
        {
            Add("p=|package=", "Path to a nuget package to releasify", v => package = v);
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
            Add("n=|signParams=", "Sign the installer via SignTool.exe with the parameters given", v => signParams = v);
            Add("f=|framework=", "Set the required .NET framework version, e.g. net461", v => framework = v);
            Add("splashImage=", "Image to be displayed during installation (can be jpg, png, gif, etc)", v => splashImage = v);
            Add("updateIcon=", "ICO file that will be used for Update.exe", v => updateIcon = v);
            Add("setupIcon=", "ICO file that will be used for Setup.exe", v => setupIcon = v);
            Add("setupName=", "The name of the app installer exe without the extension (default: 'Setup')", v => setupName = v);
            Add("noDelta", "Skip the generation of delta packages to save time", v => noDelta = true);
            Add("addSearchPath=", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc", v => HelperExe.AddSearchPath(v));
        }

        public override void Validate()
        {
            IsValidFile(nameof(setupIcon), ".ico");
            IsValidFile(nameof(updateIcon), ".ico");
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));
            IsRequired(nameof(package));
            IsValidFile(nameof(package), ".nupkg");
        }
    }

    internal class PackOptions : ReleasifyOptions
    {
        public string packName { get; private set; }
        public string packVersion { get; private set; }
        public string packAuthors { get; private set; }
        public string packDirectory { get; private set; }
        public bool includePdb { get; private set; }

        public PackOptions()
        {
            Add("packName=", "The name of the package to create", v => packName = v);
            Add("packVersion=", "Package version", v => packVersion = v);
            Add("packAuthors=", "Comma delimited list of package authors", v => packAuthors = v);
            Add("packDirectory=", "The directory with the application files that will be packaged into a release", v => packDirectory = v);
            Add("includePdb", "Include the *.pdb files in the package (default: false)", v => includePdb = true);

            // remove 'package' argument
            Remove("package");
            Remove("p");
        }

        public override void Validate()
        {
            IsRequired(nameof(packName), nameof(packVersion), nameof(packAuthors), nameof(packDirectory));
            IsValidFile(nameof(setupIcon), ".ico");
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));
        }
    }

    internal class SyncBackblazeOptions : BaseOptions
    {
        public string b2KeyId { get; private set; }
        public string b2AppKey { get; private set; }
        public string b2BucketId { get; private set; }

        public SyncBackblazeOptions()
        {
            Add("b2BucketId=", "Id or name of the bucket in B2, S3, etc", v => b2BucketId = v);
            Add("b2keyid=", "B2 Auth Key Id", v => b2KeyId = v);
            Add("b2key=", "B2 Auth Key", v => b2AppKey = v);
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
            Add("url=", "Url to the simple http folder where the releases are found", v => url = v);
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
            Add("repoUrl=", "Url to the github repository (eg. 'https://github.com/myname/myrepo')", v => repoUrl = v);
            Add("token=", "The oauth token to use as login credentials", v => token = v);
        }

        public override void Validate()
        {
            IsRequired(nameof(repoUrl));
            IsValidUrl(nameof(repoUrl));
        }
    }
}
