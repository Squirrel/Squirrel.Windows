using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            Log.Warn("Provider 'b2' is being deprecated and will no longer be updated.");
            Log.Warn("The replacement is using the 's3' provider with BackBlaze B2 using the '--endpoint' option.");
        }
    }

    internal class SyncS3Options : BaseOptions
    {
        public string keyId { get; private set; }
        public string secret { get; private set; }
        public string region { get; private set; }
        public string endpoint { get; private set; }
        public string bucket { get; private set; }
        public string pathPrefix { get; private set; }
        public bool overwrite { get; private set; }
        public int keepMaxReleases { get; private set; }

        public SyncS3Options()
        {
            Add("keyId=", "Authentication {IDENTIFIER} or access key", v => keyId = v);
            Add("secret=", "Authentication secret {KEY}", v => secret = v);
            Add("region=", "AWS service {REGION} (eg. us-west-1)", v => region = v);
            Add("endpoint=", "Custom service {URL} (backblaze, digital ocean, etc)", v => endpoint = v);
            Add("bucket=", "{NAME} of the S3 bucket", v => bucket = v);
            Add("pathPrefix=", "A sub-folder {PATH} used for files in the bucket, for creating release channels (eg. 'stable' or 'dev')", v => pathPrefix = v);
            Add("overwrite", "Replace existing files if source has changed", v => overwrite = true);
            Add("keepMaxReleases=", "Applies a retention policy during upload which keeps only the specified {NUMBER} of old versions",
                v => keepMaxReleases = ParseIntArg(nameof(keepMaxReleases), v));
        }

        public override void Validate()
        {
            IsRequired(nameof(secret), nameof(keyId), nameof(bucket));

            if ((region == null) == (endpoint == null)) {
                throw new OptionValidationException("One of 'region' and 'endpoint' arguments is required and are also mutually exclusive. Specify only one of these. ");
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
