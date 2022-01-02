using System;

namespace Squirrel
{
    public class ChecksumFailedException : Exception
    {
        public string Filename { get; set; }
    }
}
