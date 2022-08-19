using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Squirrel.Tool
{
    public class NugetDownloader
    {
        private readonly ILogger _logger;
        private readonly PackageSource _packageSource;
        private readonly SourceRepository _sourceRepository;
        private readonly SourceCacheContext _sourceCacheContext;

        public NugetDownloader(ILogger logger)
        {
            _logger = logger;
            _packageSource = new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org");
            _sourceRepository = new SourceRepository(_packageSource, Repository.Provider.GetCoreV3());
            _sourceCacheContext = new SourceCacheContext();
        }

        public IPackageSearchMetadata GetPackageMetadata(string packageName, string version)
        {
            PackageMetadataResource packageMetadataResource = _sourceRepository.GetResource<PackageMetadataResource>();
            FindPackageByIdResource packageByIdResource = _sourceRepository.GetResource<FindPackageByIdResource>();
            IPackageSearchMetadata package = null;

            var prerelease = version?.Equals("pre", StringComparison.InvariantCultureIgnoreCase) == true;
            if (version == null || version.Equals("latest", StringComparison.InvariantCultureIgnoreCase) || prerelease) {
                // get latest (or prerelease) version
                IEnumerable<IPackageSearchMetadata> metadata = packageMetadataResource
                    .GetMetadataAsync(packageName, true, true, _sourceCacheContext, _logger, CancellationToken.None)
                    .GetAwaiter().GetResult();
                package = metadata
                    .Where(x => x.IsListed)
                    .Where(x => prerelease || !x.Identity.Version.IsPrerelease)
                    .OrderByDescending(x => x.Identity.Version)
                    .FirstOrDefault();
            } else {
                // resolve version ranges and wildcards
                var versions = packageByIdResource.GetAllVersionsAsync(packageName, _sourceCacheContext, _logger, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var resolved = versions.FindBestMatch(VersionRange.Parse(version), version => version);

                // get exact version
                var packageIdentity = new PackageIdentity(packageName, resolved);
                package = packageMetadataResource
                    .GetMetadataAsync(packageIdentity, _sourceCacheContext, _logger, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            if (package == null) {
                throw new Exception($"Unable to locate {packageName} {version} on NuGet.org");
            }

            return package;
        }

        public void DownloadPackageToStream(IPackageSearchMetadata package, Stream targetStream)
        {
            FindPackageByIdResource packageByIdResource = _sourceRepository.GetResource<FindPackageByIdResource>();
            packageByIdResource
                .CopyNupkgToStreamAsync(package.Identity.Id, package.Identity.Version, targetStream, _sourceCacheContext, _logger, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
    }
}