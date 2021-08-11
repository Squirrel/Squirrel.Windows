using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Web.XmlTransform;
using NuGet.Resources;

namespace NuGet
{
    internal class XdtTransformer : IPackageFileTransformer
    {
        public XdtTransformer()
        {
        }

        public void TransformFile(IPackageFile file, string targetPath, IProjectSystem projectSystem)
        {
            PerformXdtTransform(file, targetPath, projectSystem);
        }

        public void RevertFile(IPackageFile file, string targetPath, IEnumerable<IPackageFile> matchingFiles, IProjectSystem projectSystem)
        {
            PerformXdtTransform(file, targetPath, projectSystem);
        }

        private static void PerformXdtTransform(IPackageFile file, string targetPath, IProjectSystem projectSystem)
        {
            if (projectSystem.FileExists(targetPath))
            {
                string content = Preprocessor.Process(file, projectSystem);

                try
                {
                    using (var transformation = new XmlTransformation(content, isTransformAFile: false, logger: null))
                    {
                        using (var document = new XmlTransformableDocument())
                        {
                            document.PreserveWhitespace = true;

                            // make sure we close the input stream immediately so that we can override 
                            // the file below when we save to it.
                            using (var inputStream = projectSystem.OpenFile(targetPath))
                            {
                                document.Load(inputStream);
                            }

                            bool succeeded = transformation.Apply(document);
                            if (succeeded)
                            {
                                using (var memoryStream = new MemoryStream())
                                {
                                    // save the result into a memoryStream first so that if there is any
                                    // exception during document.Save(), the original file won't be truncated.
                                    document.Save(memoryStream);
                                    memoryStream.Seek(0, SeekOrigin.Begin);
                                    using (var fileStream = projectSystem.CreateFile(targetPath))
                                    {
                                        memoryStream.CopyTo(fileStream);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidDataException(
                        String.Format(
                            CultureInfo.CurrentCulture, 
                            NuGetResources.XdtError + " " + exception.Message,
                            targetPath, 
                            projectSystem.ProjectName), 
                        exception);
                }
            }
        }
    }
}