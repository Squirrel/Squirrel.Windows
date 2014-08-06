using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel
{
    public interface IUpdateManager : IDisposable
    {
        /// <summary>
        /// Fetch the remote store for updates and compare against the current 
        /// version to determine what updates to download.
        /// </summary>
        /// <param name="ignoreDeltaUpdates">Set this flag if applying a release
        /// fails to fall back to a full release, which takes longer to download
        /// but is less error-prone.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>An UpdateInfo object representing the updates to install.
        /// </returns>
        Task<UpdateInfo> CheckForUpdate(bool ignoreDeltaUpdates, Action<int> progress = null);

        /// <summary>
        /// Download a list of releases into the local package directory.
        /// </summary>
        /// <param name="releasesToDownload">The list of releases to download, 
        /// almost always from UpdateInfo.ReleasesToApply.</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        /// <returns>A completion Observable - either returns a single 
        /// Unit.Default then Complete, or Throw</returns>
        Task DownloadReleases(IEnumerable<ReleaseEntry> releasesToDownload, Action<int> progress = null);

        /// <summary>
        /// Take an already downloaded set of releases and apply them, 
        /// copying in the new files from the NuGet package and rewriting 
        /// the application shortcuts.
        /// </summary>
        /// <param name="updateInfo">The UpdateInfo instance acquired from 
        /// CheckForUpdate</param>
        /// <param name="progress">A Observer which can be used to report Progress - 
        /// will return values from 0-100 and Complete, or Throw</param>
        Task ApplyReleases(UpdateInfo updateInfo, Action<int> progress = null);
    }

    public static class EasyModeMixin
    {
        public static async Task<ReleaseEntry> UpdateApp(this IUpdateManager This, Action<int> progress = null)
        {
            progress = progress ?? (_ => {});

            var updateInfo = await This.CheckForUpdate(false, x => progress(x / 3));
            await This.DownloadReleases(updateInfo.ReleasesToApply, x => progress(x / 3 + 33));
            await This.ApplyReleases(updateInfo, x => progress(x / 3 + 66));

            return updateInfo.ReleasesToApply.MaxBy(x => x.Version).LastOrDefault();
        }
    }
}
