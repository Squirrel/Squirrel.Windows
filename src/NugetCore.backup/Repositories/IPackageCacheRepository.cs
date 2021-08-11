using System;
using System.IO;

namespace NuGet
{
    public interface IPackageCacheRepository : IPackageRepository
    {
        /// <summary>
        /// Invokes the specified action, providing it a stream to write data to in order to add the specified package
        /// to the cache. Returns true if the action succeeds and the package is successfully inserted into the cache.
        /// Returns false if the action succeeds but the package cannot be inserted into the cache. Throws if the action
        /// throws.
        /// </summary>
        /// <param name="packageId">The ID of the package being downloaded (used to form the cache key/file name)</param>
        /// <param name="version">The version of the package being downloaded (used to form the cache key/file name)</param>
        /// <param name="action">The action to take in order to download the package</param>
        /// <returns>True if the action succeeds and the package is successfully inserted into the cache, false if the action succeeds but the package cannot be inserted into the cache</returns>
        bool InvokeOnPackage(string packageId, SemanticVersion version, Action<Stream> action);
    }
}