using System;

namespace NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2237:MarkISerializableTypesWithSerializable"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors")]
    public class PackageNotInstalledException : Exception
    {
        public PackageNotInstalledException()
        {
        }

        public PackageNotInstalledException(string message)
            : base(message)
        {
        }

        public PackageNotInstalledException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }
    }
}
