using System;

namespace Squirrel
{
    /// <summary>
    /// Represents an error that occurs when a package does not match it's expected SHA checksum
    /// </summary>
    public class ChecksumFailedException : Exception
    {
        /// <summary>
        /// The filename of the package which failed validation
        /// </summary>
        public string Filename { get; set; }
    }
}
