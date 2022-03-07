using System;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel.Sources
{
    /// <summary>
    /// Abstraction for finding and downloading updates from a package source / repository.
    /// An implementation may copy a file from a local repository, download from a web address, 
    /// or even use third party services and parse proprietary data to produce a package feed.
    /// </summary>
    public interface IUpdateSource : IEnableLogger
    {
        /// <summary>
        /// Retrieve the list of available remote releases from the package source. These releases
        /// can subsequently be downloaded with <see cref="DownloadReleaseEntry(ReleaseEntry, string, Action{int})"/>.
        /// </summary>
        /// <param name="stagingId">A persistent user-id, used for calculating whether a specific
        /// release should be available to this user or not. (eg, for the purposes of rolling out
        /// an update to only a small portion of users at a time).</param>
        /// <param name="latestLocalRelease">The latest / current local release. If specified,
        /// metadata from this package may be provided to the remote server (such as package id,
        /// or cpu architecture) to ensure that the correct package is downloaded for this user.
        /// </param>
        /// <returns>An array of <see cref="ReleaseEntry"/> objects that are available for download
        /// and are applicable to this user.</returns>
        Task<ReleaseEntry[]> GetReleaseFeed(Guid? stagingId = null, ReleaseEntry latestLocalRelease = null);

        /// <summary>
        /// Download the specified <see cref="ReleaseEntry"/> to the provided local file path.
        /// </summary>
        /// <param name="releaseEntry">The release to download.</param>
        /// <param name="localFile">The path on the local disk to store the file. If this file exists,
        /// it will be overwritten.</param>
        /// <param name="progress">This delegate will be executed with values from 0-100 as the
        /// download is being processed.</param>
        Task DownloadReleaseEntry(ReleaseEntry releaseEntry, string localFile, Action<int> progress);
    }
}
