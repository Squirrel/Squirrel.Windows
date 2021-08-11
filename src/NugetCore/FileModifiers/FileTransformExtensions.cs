using System;

namespace NuGet
{
    public sealed class FileTransformExtensions : IEquatable<FileTransformExtensions>
    {
        public string InstallExtension { get; private set; }
        public string UninstallExtension { get; private set; }

        public FileTransformExtensions(string installExtension, string uninstallExtension)
        {
            if (String.IsNullOrEmpty(installExtension))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "installExtension");
            }

            if (String.IsNullOrEmpty(uninstallExtension))
            {
                throw new ArgumentException(CommonResources.Argument_Cannot_Be_Null_Or_Empty, "uninstallExtension");
            }

            InstallExtension = installExtension;
            UninstallExtension = uninstallExtension;
        }

        public bool Equals(FileTransformExtensions other)
        {
            return String.Equals(InstallExtension, other.InstallExtension, StringComparison.OrdinalIgnoreCase) &&
                   String.Equals(UninstallExtension, other.UninstallExtension, StringComparison.OrdinalIgnoreCase);
        }

        public override int GetHashCode()
        {
            return InstallExtension.GetHashCode() * 3137 + UninstallExtension.GetHashCode();
        }
    }
}
