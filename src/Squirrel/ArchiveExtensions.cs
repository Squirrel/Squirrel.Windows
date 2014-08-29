using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel
{
    public static class ArchiveExtensions
    {
        public static ZipArchiveEntry CreateDirectory(this ZipArchive archive, string directoryName)
        {
            if (!directoryName.EndsWith(Path.DirectorySeparatorChar.ToString()))
                directoryName += Path.DirectorySeparatorChar;

            return archive.CreateEntry(directoryName);
        }
    }
}
