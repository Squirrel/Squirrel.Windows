using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    [DataContract]
    public class UpdateInfo : IEnableLogger
    {
        [DataMember] public ReleaseEntry CurrentlyInstalledVersion { get; protected set; }
        [DataMember] public ReleaseEntry FutureReleaseEntry { get; protected set; }
        [DataMember] public List<ReleaseEntry> ReleasesToApply { get; protected set; }

        [IgnoreDataMember]
        public bool IsBootstrapping {
            get { return CurrentlyInstalledVersion == null;  }
        }

        [IgnoreDataMember]
        public string PackageDirectory { get; protected set; }

        protected UpdateInfo(ReleaseEntry currentlyInstalledVersion, IEnumerable<ReleaseEntry> releasesToApply, string packageDirectory)
        {
            // NB: When bootstrapping, CurrentlyInstalledVersion is null!
            CurrentlyInstalledVersion = currentlyInstalledVersion;
            ReleasesToApply = (releasesToApply ?? Enumerable.Empty<ReleaseEntry>()).ToList();
            FutureReleaseEntry = ReleasesToApply.Any() ?
                ReleasesToApply.MaxBy(x => x.Version).FirstOrDefault() :
                CurrentlyInstalledVersion;

            this.PackageDirectory = packageDirectory;
        }

        public Dictionary<ReleaseEntry, string> FetchReleaseNotes()
        {
            return ReleasesToApply
                .SelectMany(x => {
                    try {
                        var releaseNotes = x.GetReleaseNotes(PackageDirectory);
                        return EnumerableExtensions.Return(Tuple.Create(x, releaseNotes));
                    } catch (Exception ex) {
                        this.Log().WarnException("Couldn't get release notes for:" + x.Filename, ex);
                        return Enumerable.Empty<Tuple<ReleaseEntry, string>>();
                    }
                })
                .ToDictionary(k => k.Item1, v => v.Item2);
        }

        public static UpdateInfo Create(ReleaseEntry currentVersion, IEnumerable<ReleaseEntry> availableReleases, string packageDirectory)
        {
            Contract.Requires(availableReleases != null);
            Contract.Requires(!String.IsNullOrEmpty(packageDirectory));

            var latestFull = availableReleases.MaxBy(x => x.Version).FirstOrDefault(x => !x.IsDelta);
            if (latestFull == null) {
                throw new Exception("There should always be at least one full release");
            }

            if (currentVersion == null) {
                return new UpdateInfo(null, new[] { latestFull }, packageDirectory);
            }

            if (currentVersion.Version >= latestFull.Version) {
                return new UpdateInfo(currentVersion, Enumerable.Empty<ReleaseEntry>(), packageDirectory);
            }

            var newerThanUs = availableReleases
                .Where(x => x.Version > currentVersion.Version)
                .OrderBy(v => v.Version);

            var deltasSize = newerThanUs.Where(x => x.IsDelta).Sum(x => x.Filesize);

            return (deltasSize < latestFull.Filesize && deltasSize > 0) ? 
                new UpdateInfo(currentVersion, newerThanUs.Where(x => x.IsDelta).ToArray(), packageDirectory) : 
                new UpdateInfo(currentVersion, new[] { latestFull }, packageDirectory);
        }
    }
}
