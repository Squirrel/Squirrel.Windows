using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public string splashImage { get; private set; }
        public string iconPath { get; private set; }
        public string signParams { get; private set; }
        public string framework { get; private set; }
        public bool noDelta { get; private set; }
        public string baseUrl { get; private set; }

        public ReleasifyOptions()
        {
            Add("p=|package=", "Path to a nuget package to releasify", v => package = v);
            Add("s=|splashImage=", "Image to be displayed during installation (can be jpg, png, gif, etc)", v => splashImage = v);
            Add("i=|iconPath=", "Ico file that will be used where possible", v => iconPath = v);
            Add("n=|signParams=", "Sign the installer via SignTool.exe with the parameters given", v => signParams = v);
            Add("f=|framework=", "Set the required .NET framework version, e.g. net461", v => framework = v);
            Add("no-delta", "Don't generate delta packages to save time", v => noDelta = true);
            Add("b=|baseUrl=", "Provides a base URL to prefix the RELEASES file packages with", v => baseUrl = v, true);
        }

        public override void Validate()
        {
            IsValidFile(nameof(iconPath));
            IsValidFile(nameof(splashImage));
            IsValidUrl(nameof(baseUrl));
            IsRequired(nameof(package));
            IsValidFile(nameof(package));
        }
    }

    internal class PackOptions : ReleasifyOptions
    {
        public string packName { get; private set; }
        public string packVersion { get; private set; }
        public string packAuthors { get; private set; }
        public string packDirectory { get; private set; }

        public PackOptions()
        {
            Add("packName=", "desc", v => packName = v);
            Add("packVersion=", "desc", v => packVersion = v);
            Add("packAuthors=", "desc", v => packAuthors = v);
            Add("packDirectory=", "desc", v => packDirectory = v);

            // remove 'package' argument
            Remove("package"); 
            Remove("p"); 
        }

        public override void Validate()
        {
            IsRequired(nameof(packName), nameof(packVersion), nameof(packAuthors), nameof(packDirectory));
            IsValidFile(nameof(iconPath));
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
        }
    }
}
