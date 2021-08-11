using System;

namespace NuGet
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1032:ImplementStandardExceptionConstructors", Justification="We don't need the constructor which accepts streaming context.")]
    [Serializable]
    public class NuGetVersionNotSatisfiedException : Exception
    {
        public NuGetVersionNotSatisfiedException()
        {
        }

        public NuGetVersionNotSatisfiedException(string message)
            : base(message)
        {
        }

        public NuGetVersionNotSatisfiedException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}