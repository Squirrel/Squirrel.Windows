using System.IO;
using System.Runtime.Versioning;

namespace NuGet
{
    public interface IProjectSystem : IFileSystem, IPropertyProvider
    {
        FrameworkName TargetFramework { get; }
        string ProjectName { get; }

        /// <summary>
        /// Method called when adding an assembly reference to the project.
        /// </summary>
        /// <param name="referencePath">Physical path to the assembly file relative to the project root.</param>
        void AddReference(string referencePath);

        /// <summary>
        /// Adds an assembly reference to a framework assembly (one in the GAC).
        /// </summary>
        /// <param name="name">name of the assembly</param>
        void AddFrameworkReference(string name);
        bool ReferenceExists(string name);
        void RemoveReference(string name);
        bool IsSupportedFile(string path);
        string ResolvePath(string path);
        bool IsBindingRedirectSupported { get; }
        void AddImport(string targetFullPath, ProjectImportLocation location);
        void RemoveImport(string targetFullPath);
        bool FileExistsInProject(string path);
    }
}
