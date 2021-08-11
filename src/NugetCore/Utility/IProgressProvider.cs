using System;

namespace NuGet
{
    public interface IProgressProvider
    {
        event EventHandler<ProgressEventArgs> ProgressAvailable;
    }
}
