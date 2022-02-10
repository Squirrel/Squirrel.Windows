using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Squirrel;
using Squirrel.Lib;
using Squirrel.SimpleSplat;

namespace SquirrelCli
{
    internal abstract class BaseOptions : ValidatedOptionSet
    {
        public string releaseDir { get; private set; } = ".\\Releases";

        protected static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(BaseOptions));

        public BaseOptions()
        {
            Add("r=|releaseDir=", "Output {DIRECTORY} for releasified packages", v => releaseDir = v);
        }
    }

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

        public override void Validate()
        {
            if (!String.IsNullOrEmpty(signParams) && !String.IsNullOrEmpty(signTemplate)) {
                throw new OptionValidationException($"Cannot use 'signParams' and 'signTemplate' options together, please choose one or the other.");
            }

            if (!String.IsNullOrEmpty(signTemplate) && !signTemplate.Contains("{{file}}")) {
                throw new OptionValidationException($"Argument 'signTemplate': Must contain '{{{{file}}}}' in template string (replaced with the file to sign). Current value is '{signTemplate}'");
            }
        }

        public void SignPEFile(string filePath)
        {
            try {
                if (AuthenticodeTools.IsTrusted(filePath)) {
                    Log.Debug("'{0}' is already signed, skipping...", filePath);
                    return;
                }
            } catch (Exception ex) {
                Log.ErrorException("Failed to determine signing status for " + filePath, ex);
            }

            string cmd;
            ProcessStartInfo psi;
            if (!String.IsNullOrEmpty(signParams)) {
                // use embedded signtool.exe with provided parameters
                cmd = $"sign {signParams} \"{filePath}\"";
                psi = Utility.CreateProcessStartInfo(HelperExe.SignToolPath, cmd);
                cmd = "signtool.exe " + cmd;
            } else if (!String.IsNullOrEmpty(signTemplate)) {
                // escape custom sign command and pass it to cmd.exe
                cmd = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");
                psi = Utility.CreateProcessStartInfo("cmd", $"/c {Utility.EscapeCmdExeMetachars(cmd)}");
            } else {
                Log.Debug("{0} was not signed. (skipped; no signing parameters)", filePath);
                return;
            }

            var processResult = Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            if (processResult.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"/p\s+\w+").Replace(cmd, "/p ********");
                throw new Exception("Signing command failed: \n > " + cmdWithPasswordHidden + "\n" + processResult.StdOutput);
            } else {
                Log.Info("Sign successful: " + processResult.StdOutput);
            }
        }
    }

    internal class ReleasifyOptions : SigningOptions
    {
        public string package { get; set; }
        public string baseUrl { get; private set; }
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
            Add("addSearchPath=", "Add additional search directories when looking for helper exe's such as Setup.exe, Update.exe, etc",
                v => HelperExe.AddSearchPath(v), true);

            // public arguments
            InsertAt(1, "p=|package=", "{PATH} to a '.nupkg' package to releasify", v => package = v);
            Add("noDelta", "Skip the generation of delta packages", v => noDelta = true);
            Add("f=|framework=", "List of required {RUNTIMES} to install during setup\nexample: 'net6,vcredist143'", v => framework = v);
            Add("s=|splashImage=", "{PATH} to image/gif displayed during installation", v => splashImage = v);
            Add("i=|icon=", "{PATH} to .ico for Setup.exe and Update.exe",
                (v) => { updateIcon = v; setupIcon = v; });
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

    internal class SyncS3Options : BaseOptions
    {
        public string key { get; private set; }
        public string secret { get; private set; }
        public string region { get; private set; }
        public string endpointUrl { get; private set; }
        public string bucket { get; private set; }
        public string pathPrefix { get; private set; }
        public bool overwrite { get; private set; }

        public SyncS3Options() 
        {
            Add("key=", "Authentication {IDENTIFIER} or access key", v => key = v);
            Add("secret=", "Authentication secret {KEY}", v => secret = v);
            Add("region=", "AWS service {REGION} (eg. us-west-1)", v => region = v);
            Add("endpointUrl=", "Custom service {URL} (from backblaze, digital ocean, etc)", v => endpointUrl = v);
            Add("bucket=", "{NAME} of the S3 bucket to access", v => bucket = v);
            Add("pathPrefix=", "A sub-folder {PATH} to read and write files in", v => pathPrefix = v);
            Add("overwrite", "Replace any mismatched remote files with files in local directory", v => overwrite = true);
        }

        public override void Validate()
        {
            IsRequired(nameof(secret), nameof(key), nameof(bucket));
            IsValidUrl(nameof(endpointUrl));

            if ((region == null) == (endpointUrl == null)) {
                throw new OptionValidationException("One of 'region' and 'endpoint' arguments is required and are also mutually exclusive. Specify one of these. ");
            }

            if (region != null) {
                var r = Amazon.RegionEndpoint.GetBySystemName(region);
                if (r.DisplayName == "Unknown")
                    Log.Warn($"Region '{region}' lookup failed, is this a valid AWS region?");
            }
        }
    }

    internal class SyncHttpOptions : BaseOptions
    {
        public string url { get; private set; }
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
        public bool pre { get; private set; }

        public SyncGithubOptions()
        {
            Add("repoUrl=", "Full url to the github repository\nexample: 'https://github.com/myname/myrepo'", v => repoUrl = v);
            Add("token=", "OAuth token to use as login credentials", v => token = v);
            Add("pre", "Fetch the latest pre-release instead of stable", v => pre = true);
        }

        public override void Validate()
        {
            IsRequired(nameof(repoUrl));
            IsValidUrl(nameof(repoUrl));
        }
    }
}
