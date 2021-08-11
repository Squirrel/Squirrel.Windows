using System;

namespace NuGet
{
    public class ProgressEventArgs : EventArgs
    {
        public ProgressEventArgs(int percentComplete)
            : this(null, percentComplete)
        {
        }

        public ProgressEventArgs(string operation, int percentComplete)
        {
            Operation = operation;
            PercentComplete = percentComplete;
        }

        public string Operation { get; private set; }
        public int PercentComplete { get; private set; }
    }
}
