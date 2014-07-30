using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;

namespace Squirrel
{
    public enum FrameworkVersion {
        Net40,
        Net45,
    }

    [DataContract]
    public class UpdateInfo
    {
        [DataMember] public ReleaseEntry CurrentlyInstalledVersion { get; protected set; }
        [DataMember] public ReleaseEntry FutureReleaseEntry { get; protected set; }
        [DataMember] public List<ReleaseEntry> ReleasesToApply { get; protected set; }
        [DataMember] public FrameworkVersion AppFrameworkVersion { get; protected set; }

        [IgnoreDataMember]
        public bool IsBootstrapping {
            get { return CurrentlyInstalledVersion == null;  }
        }

        [IgnoreDataMember]
        public string PackageDirectory { get; protected set; }

        protected UpdateInfo(ReleaseEntry currentlyInstalledVersion, IEnumerable<ReleaseEntry> releasesToApply, string packageDirectory, FrameworkVersion appFrameworkVersion)
        {
            // NB: When bootstrapping, CurrentlyInstalledVersion is null!
            CurrentlyInstalledVersion = currentlyInstalledVersion;
            ReleasesToApply = (releasesToApply ?? Enumerable.Empty<ReleaseEntry>()).ToList();
            FutureReleaseEntry = ReleasesToApply.Any()
                    ? ReleasesToApply.MaxBy(x => x.Version).FirstOrDefault()
                    : null;
            AppFrameworkVersion = appFrameworkVersion;

            this.PackageDirectory = packageDirectory;
        }

        public Dictionary<ReleaseEntry, string> FetchReleaseNotes()
        {
            return ReleasesToApply
                .Select(x => new { Entry = x, Readme = x.GetReleaseNotes(PackageDirectory) })
                .ToDictionary(k => k.Entry, v => v.Readme);
        }

        public static UpdateInfo Create(ReleaseEntry currentVersion, IEnumerable<ReleaseEntry> availableReleases, string packageDirectory, FrameworkVersion appFrameworkVersion)
        {
            Contract.Requires(availableReleases != null);
            Contract.Requires(!String.IsNullOrEmpty(packageDirectory));

            var latestFull = availableReleases.MaxBy(x => x.Version).FirstOrDefault(x => !x.IsDelta);
            if (latestFull == null) {
                throw new Exception("There should always be at least one full release");
            }

            if (currentVersion == null) {
                return new UpdateInfo(currentVersion, new[] { latestFull }, packageDirectory, appFrameworkVersion);
            }

            if (currentVersion.Version == latestFull.Version) {
                return new UpdateInfo(currentVersion, Enumerable.Empty<ReleaseEntry>(), packageDirectory, appFrameworkVersion);
            }

            var newerThanUs = availableReleases.Where(x => x.Version > currentVersion.Version)
                                               .OrderBy(v => v.Version);
            var deltasSize = newerThanUs.Where(x => x.IsDelta).Sum(x => x.Filesize);

            return (deltasSize < latestFull.Filesize && deltasSize > 0)
                ? new UpdateInfo(currentVersion, newerThanUs.Where(x => x.IsDelta).ToArray(), packageDirectory, appFrameworkVersion)
                : new UpdateInfo(currentVersion, new[] { latestFull }, packageDirectory, appFrameworkVersion);
        }
    }
}
