using System;

namespace Squirrel
{
    /// <inheritdoc />
    /// <summary>
    /// Base class for Squirrel-originated exceptions.
    /// </summary>
    public abstract class SquirrelBaseException: Exception
    {
        protected SquirrelBaseException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when no Releases folder or RELEASES file is found at the specified location.
    /// </summary>
    public class SquirrelReleasesMissingException : SquirrelBaseException
    {
        public SquirrelReleasesMissingException(string message) 
            : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when the Releases\RELEASES file is empty or corrupt.
    /// </summary>
    public class SquirrelReleasesCorruptException: SquirrelBaseException
    {
        public SquirrelReleasesCorruptException(string message) 
            : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when Update.exe isn't found in the expected location.
    /// </summary>
    /// <remarks>See https://github.com/Squirrel/Squirrel.Windows/blob/master/docs/using/debugging-updates.md for more information.</remarks>
    public class SquirrelNoUpdateException: SquirrelBaseException
    {
        public SquirrelNoUpdateException(string message) 
            : base(message)
        {
        }
    }
}
