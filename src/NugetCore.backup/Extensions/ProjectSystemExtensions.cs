using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.Resources;

namespace NuGet
{
    public static class ProjectSystemExtensions
    {
        public static void AddFiles(this IProjectSystem project,
                                    IEnumerable<IPackageFile> files,
                                    IDictionary<FileTransformExtensions, IPackageFileTransformer> fileTransformers)
        {
            // Convert files to a list
            List<IPackageFile> fileList = files.ToList();

            // See if the project system knows how to sort the files
            var fileComparer = project as IComparer<IPackageFile>;

            if (fileComparer != null)
            {
                fileList.Sort(fileComparer);
            }

            var batchProcessor = project as IBatchProcessor<string>;

            try
            {
                if (batchProcessor != null)
                {
                    var paths = fileList.Select(file => ResolvePath(fileTransformers, fte => fte.InstallExtension, file.EffectivePath));
                    batchProcessor.BeginProcessing(paths, PackageAction.Install);
                }

                foreach (IPackageFile file in fileList)
                {
                    if (file.IsEmptyFolder())
                    {
                        continue;
                    }

                    // Resolve the target path
                    IPackageFileTransformer installTransformer;
                    string path = ResolveTargetPath(project, fileTransformers, fte => fte.InstallExtension, file.EffectivePath, out installTransformer);

                    if (project.IsSupportedFile(path))
                    {
                        if (installTransformer != null)
                        {
                            installTransformer.TransformFile(file, path, project);
                        }
                        else
                        {
                            // Ignore uninstall transform file during installation
                            string truncatedPath;
                            IPackageFileTransformer uninstallTransformer =
                                FindFileTransformer(fileTransformers, fte => fte.UninstallExtension, file.EffectivePath, out truncatedPath);
                            if (uninstallTransformer != null)
                            {
                                continue;
                            }
                            TryAddFile(project, path, file.GetStream);
                        }
                    }
                }
            }
            finally
            {
                if (batchProcessor != null)
                {
                    batchProcessor.EndProcessing();
                }
            }
        }

        /// <summary>
        /// Try to add the specified the project with the target path. If there's an existing file in the project with the same name, 
        /// it will ask the logger for the resolution, which has 4 choices: Overwrite|Ignore|Overwrite All|Ignore All
        /// </summary>
        internal static void TryAddFile(IProjectSystem project, string path, Func<Stream> content)
        {
            if (project.FileExists(path) && project.FileExistsInProject(path))
            {
                // file exists in project, ask user if he wants to overwrite or ignore
                string conflictMessage = String.Format(CultureInfo.CurrentCulture, NuGetResources.FileConflictMessage, path, project.ProjectName);
                FileConflictResolution resolution = project.Logger.ResolveFileConflict(conflictMessage);
                if (resolution == FileConflictResolution.Overwrite || resolution == FileConflictResolution.OverwriteAll)
                {
                    // overwrite
                    project.Logger.Log(MessageLevel.Info, NuGetResources.Info_OverwriteExistingFile, path);
                    using (Stream stream = content())
                    {
                        project.AddFile(path, stream);
                    }
                }
                else
                {
                    // ignore
                    project.Logger.Log(MessageLevel.Info, NuGetResources.Warning_FileAlreadyExists, path);
                }
            }
            else
            {
                using (Stream stream = content())
                {
                    project.AddFile(path, stream);
                }
            }
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want delete to be robust, when exceptions occur we log then and move on")]
        public static void DeleteFiles(this IProjectSystem project,
                                       IEnumerable<IPackageFile> files,
                                       IEnumerable<IPackage> otherPackages,
                                       IDictionary<FileTransformExtensions, IPackageFileTransformer> fileTransformers)
        {
            IPackageFileTransformer transformer;
            // First get all directories that contain files
            var directoryLookup = files.ToLookup(
                p => Path.GetDirectoryName(ResolveTargetPath(project, fileTransformers, fte => fte.UninstallExtension, p.EffectivePath, out transformer)));

            // Get all directories that this package may have added
            var directories = from grouping in directoryLookup
                              from directory in FileSystemExtensions.GetDirectories(grouping.Key)
                              orderby directory.Length descending
                              select directory;

            // Remove files from every directory
            foreach (var directory in directories)
            {
                var directoryFiles = directoryLookup.Contains(directory) ? directoryLookup[directory] : Enumerable.Empty<IPackageFile>();

                if (!project.DirectoryExists(directory))
                {
                    continue;
                }
                var batchProcessor = project as IBatchProcessor<string>;

                try
                {
                    if (batchProcessor != null)
                    {
                        var paths = directoryFiles.Select(file => ResolvePath(fileTransformers, fte => fte.UninstallExtension, file.EffectivePath));
                        batchProcessor.BeginProcessing(paths, PackageAction.Uninstall);
                    }

                    foreach (var file in directoryFiles)
                    {
                        if (file.IsEmptyFolder())
                        {
                            continue;
                        }

                        // Resolve the path
                        string path = ResolveTargetPath(project,
                                                        fileTransformers,
                                                        fte => fte.UninstallExtension,
                                                        file.EffectivePath,
                                                        out transformer);

                        if (project.IsSupportedFile(path))
                        {
                            if (transformer != null)
                            {
                                var matchingFiles = from p in otherPackages
                                                    from otherFile in project.GetCompatibleItemsCore(p.GetContentFiles())
                                                    where otherFile.EffectivePath.Equals(file.EffectivePath, StringComparison.OrdinalIgnoreCase)
                                                    select otherFile;

                                try
                                {
                                    transformer.RevertFile(file, path, matchingFiles, project);
                                }
                                catch (Exception e)
                                {
                                    // Report a warning and move on
                                    project.Logger.Log(MessageLevel.Warning, e.Message);
                                }
                            }
                            else
                            {
                                project.DeleteFileSafe(path, file.GetStream);
                            }
                        }
                    }

                    // If the directory is empty then delete it
                    if (!project.GetFilesSafe(directory).Any() &&
                        !project.GetDirectoriesSafe(directory).Any())
                    {
                        project.DeleteDirectorySafe(directory, recursive: false);
                    }
                }
                finally
                {
                    if (batchProcessor != null)
                    {
                        batchProcessor.EndProcessing();
                    }
                }
            }
        }

        public static bool TryGetCompatibleItems<T>(this IProjectSystem projectSystem, IEnumerable<T> items, out IEnumerable<T> compatibleItems) where T : IFrameworkTargetable
        {
            if (projectSystem == null)
            {
                throw new ArgumentNullException("projectSystem");
            }

            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            return VersionUtility.TryGetCompatibleItems<T>(projectSystem.TargetFramework, items, out compatibleItems);
        }

        internal static IEnumerable<T> GetCompatibleItemsCore<T>(this IProjectSystem projectSystem, IEnumerable<T> items) where T : IFrameworkTargetable
        {
            IEnumerable<T> compatibleItems;
            if (VersionUtility.TryGetCompatibleItems(projectSystem.TargetFramework, items, out compatibleItems))
            {
                return compatibleItems;
            }
            return Enumerable.Empty<T>();
        }

        private static string ResolvePath(
            IDictionary<FileTransformExtensions, IPackageFileTransformer> fileTransformers,
            Func<FileTransformExtensions, string> extensionSelector,
            string effectivePath)
        {
            
            string truncatedPath;

            // Remove the transformer extension (e.g. .pp, .transform)
            IPackageFileTransformer transformer = FindFileTransformer(
                fileTransformers, extensionSelector, effectivePath, out truncatedPath);
            
            if (transformer != null)
            {
                effectivePath = truncatedPath;
            }

            return effectivePath;
        }

        private static string ResolveTargetPath(IProjectSystem projectSystem,
                                                IDictionary<FileTransformExtensions, IPackageFileTransformer> fileTransformers,
                                                Func<FileTransformExtensions, string> extensionSelector,
                                                string effectivePath,
                                                out IPackageFileTransformer transformer)
        {
            string truncatedPath;

            // Remove the transformer extension (e.g. .pp, .transform)
            transformer = FindFileTransformer(fileTransformers, extensionSelector, effectivePath, out truncatedPath);
            if (transformer != null)
            {
                effectivePath = truncatedPath;
            }

            return projectSystem.ResolvePath(effectivePath);
        }

        private static IPackageFileTransformer FindFileTransformer(
            IDictionary<FileTransformExtensions, IPackageFileTransformer> fileTransformers,
            Func<FileTransformExtensions, string> extensionSelector,
            string effectivePath,
            out string truncatedPath)
        {
            foreach (var transformExtensions in fileTransformers.Keys)
            {
                string extension = extensionSelector(transformExtensions);
                if (effectivePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    truncatedPath = effectivePath.Substring(0, effectivePath.Length - extension.Length);

                    // Bug 1686: Don't allow transforming packages.config.transform,
                    // but we still want to copy packages.config.transform as-is into the project.
                    string fileName = Path.GetFileName(truncatedPath);
                    if (!Constants.PackageReferenceFile.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return fileTransformers[transformExtensions];
                    }
                }
            }

            truncatedPath = effectivePath;
            return null;
        }
    }
}