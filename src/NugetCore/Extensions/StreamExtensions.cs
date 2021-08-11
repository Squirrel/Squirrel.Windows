using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace NuGet
{
    public static class StreamExtensions
    {
        public static byte[] ReadAllBytes(this Stream stream)
        {
            var memoryStream = stream as MemoryStream;
            if (memoryStream != null)
            {
                return memoryStream.ToArray();
            }
            else
            {
                using (memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }
        }

        /// <summary>
        /// Turns an existing stream into one that a stream factory that can be reopened.
        /// </summary>        
        public static Func<Stream> ToStreamFactory(this Stream stream)
        {
            byte[] buffer;

            using (var ms = new MemoryStream())
            {
                try
                {
                    stream.CopyTo(ms);
                    buffer = ms.ToArray();
                }
                finally 
                {
                    stream.Close();
                }
            }

            return () => new MemoryStream(buffer);
        }

        public static string ReadToEnd(this Stream stream)
        {
            using (var streamReader = new StreamReader(stream))
            {
                return streamReader.ReadToEnd();
            }
        }

        public static Stream AsStream(this string value)
        {
            return AsStream(value, Encoding.UTF8);
        }

        public static Stream AsStream(this string value, Encoding encoding)
        {
            return new MemoryStream(encoding.GetBytes(value));
        }

        /// <summary>
        /// Compare the content of the two streams of data, ingoring the content within the
        /// NUGET: BEGIN LICENSE TEXT and NUGET: END LICENSE TEXCT markers.
        /// </summary>
        /// <param name="stream">First stream</param>
        /// <param name="otherStream">Second stream which MUST be a seekable stream.</param>
        /// <returns>true if the two streams are considered equal.</returns>
        public static bool ContentEquals(this Stream stream, Stream otherStream)
        {
            Debug.Assert(otherStream.CanSeek);

            bool isBinaryFile = IsBinary(otherStream);
            otherStream.Seek(0, SeekOrigin.Begin);

            return isBinaryFile ? CompareBinary(stream, otherStream) : CompareText(stream, otherStream);
        }

        public static bool IsBinary(Stream stream)
        {
            // Quick and dirty trick to check if a stream represents binary content.
            // We read the first 30 bytes. If there's a character 0 in those bytes, 
            // we assume this is a binary file. 
            byte[] a = new byte[30];
            int bytesRead = stream.Read(a, 0, 30);
            int byteZeroIndex = Array.FindIndex(a, 0, bytesRead, d => d == 0);
            return byteZeroIndex >= 0;
        }

        private static bool CompareText(Stream stream, Stream otherStream)
        {
            IEnumerable<string> lines = ReadStreamLines(stream);
            IEnumerable<string> otherLines = ReadStreamLines(otherStream);

            // IMPORTANT: this comparison has to be case-sensitive, hence Ordinal instead of OrdinalIgnoreCase
            return lines.SequenceEqual(otherLines, StringComparer.Ordinal);
        }

        /// <summary>
        /// Read the specified stream and return all lines, but ignoring those within the 
        /// NUGET: BEGIN LICENSE TEXT and NUGET: END LICENSE TEXT markers, case-insenstively.
        /// </summary>
        private static IEnumerable<string> ReadStreamLines(Stream stream)
        {
            using (var reader = new StreamReader(stream))
            {
                bool hasSeenBeginLine = false;

                while (reader.Peek() != -1)
                {
                    string line = reader.ReadLine();

                    if (line.IndexOf(Constants.EndIgnoreMarker, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        hasSeenBeginLine = false;
                    }
                    else if (line.IndexOf(Constants.BeginIgnoreMarker, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        hasSeenBeginLine = true;
                    }
                    else if (!hasSeenBeginLine)
                    {
                        // the current line is not within the marker lines.
                        yield return line;
                    }
                }
            }
        }

        private static bool CompareBinary(Stream stream, Stream otherStream)
        {
            if (stream.CanSeek && otherStream.CanSeek)
            {
                if (stream.Length != otherStream.Length)
                {
                    return false;
                }
            }

            byte[] buffer = new byte[4 * 1024];
            byte[] otherBuffer = new byte[4 * 1024];

            int bytesRead = 0;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    int otherBytesRead = otherStream.Read(otherBuffer, 0, bytesRead);
                    if (bytesRead != otherBytesRead)
                    {
                        return false;
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] != otherBuffer[i])
                        {
                            return false;
                        }
                    }
                }
            }
            while (bytesRead > 0);

            return true;
        }
    }
}