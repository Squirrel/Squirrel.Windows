using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ICSharpCode.SharpZipLib.BZip2;

// Adapted from https://github.com/LogosBible/bsdiff.net/blob/master/src/bsdiff/BinaryPatchUtility.cs

namespace Squirrel.Bsdiff
{
    /*
    The original bsdiff.c source code (http://www.daemonology.net/bsdiff/) is
    distributed under the following license:

    Copyright 2003-2005 Colin Percival
    All rights reserved

    Redistribution and use in source and binary forms, with or without
    modification, are permitted providing that the following conditions
    are met:
    1. Redistributions of source code must retain the above copyright
        notice, this list of conditions and the following disclaimer.
    2. Redistributions in binary form must reproduce the above copyright
        notice, this list of conditions and the following disclaimer in the
        documentation and/or other materials provided with the distribution.

    THIS SOFTWARE IS PROVIDED BY THE AUTHOR ``AS IS'' AND ANY EXPRESS OR
    IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
    WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
    ARE DISCLAIMED.  IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY
    DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS
    OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
    HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
    STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING
    IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
    POSSIBILITY OF SUCH DAMAGE.
    */
    class BinaryPatchUtility
    {
        /// <summary>
        /// Creates a binary patch (in <a href="http://www.daemonology.net/bsdiff/">bsdiff</a> format) that can be used
        /// (by <see cref="Apply"/>) to transform <paramref name="oldData"/> into <paramref name="newData"/>.
        /// </summary>
        /// <param name="oldData">The original binary data.</param>
        /// <param name="newData">The new binary data.</param>
        /// <param name="output">A <see cref="Stream"/> to which the patch will be written.</param>
        public static void Create(byte[] oldData, byte[] newData, Stream output)
        {
            // NB: If you diff a file big enough, we blow the stack. This doesn't 
            // solve it, just buys us more space. The solution is to rewrite Split
            // using iteration instead of recursion, but that's Hard(tm).
            var ex = default(Exception);
            var t = new Thread(() =>
            {
                try
                {
                    CreateInternal(oldData, newData, output);
                }
                catch (Exception exc)
                {
                    ex = exc;
                }
            }, 40 * 1048576);

            t.Start();
            t.Join();

            if (ex != null) throw ex;
        }

        static void CreateInternal(byte[] oldData, byte[] newData, Stream output)
        {
            // check arguments
            if (oldData == null)
                throw new ArgumentNullException("oldData");
            if (newData == null)
                throw new ArgumentNullException("newData");
            if (output == null)
                throw new ArgumentNullException("output");
            if (!output.CanSeek)
                throw new ArgumentException("Output stream must be seekable.", "output");
            if (!output.CanWrite)
                throw new ArgumentException("Output stream must be writable.", "output");

            /* Header is
                0   8    "BSDIFF40"
                8   8   length of bzip2ed ctrl block
                16  8   length of bzip2ed diff block
                24  8   length of new file */
            /* File is
                0   32  Header
                32  ??  Bzip2ed ctrl block
                ??  ??  Bzip2ed diff block
                ??  ??  Bzip2ed extra block */
            byte[] header = new byte[c_headerSize];
            WriteInt64(c_fileSignature, header, 0); // "BSDIFF40"
            WriteInt64(0, header, 8);
            WriteInt64(0, header, 16);
            WriteInt64(newData.Length, header, 24);

            long startPosition = output.Position;
            output.Write(header, 0, header.Length);

            int[] I = SuffixSort(oldData);

            byte[] db = new byte[newData.Length];
            byte[] eb = new byte[newData.Length];

            int dblen = 0;
            int eblen = 0;

            using (BZip2OutputStream bz2Stream = new BZip2OutputStream(new WrappingStream(output, Ownership.None)))
            {
                // compute the differences, writing ctrl as we go
                int scan = 0;
                int pos = 0;
                int len = 0;
                int lastscan = 0;
                int lastpos = 0;
                int lastoffset = 0;
                while (scan < newData.Length)
                {
                    int oldscore = 0;

                    for (int scsc = scan += len; scan < newData.Length; scan++)
                    {
                        len = Search(I, oldData, newData, scan, 0, oldData.Length, out pos);

                        for (; scsc < scan + len; scsc++)
                        {
                            if ((scsc + lastoffset < oldData.Length) && (oldData[scsc + lastoffset] == newData[scsc]))
                                oldscore++;
                        }

                        if ((len == oldscore && len != 0) || (len > oldscore + 8))
                            break;

                        if ((scan + lastoffset < oldData.Length) && (oldData[scan + lastoffset] == newData[scan]))
                            oldscore--;
                    }

                    if (len != oldscore || scan == newData.Length)
                    {
                        int s = 0;
                        int sf = 0;
                        int lenf = 0;
                        for (int i = 0; (lastscan + i < scan) && (lastpos + i < oldData.Length); )
                        {
                            if (oldData[lastpos + i] == newData[lastscan + i])
                                s++;
                            i++;
                            if (s * 2 - i > sf * 2 - lenf)
                            {
                                sf = s;
                                lenf = i;
                            }
                        }

                        int lenb = 0;
                        if (scan < newData.Length)
                        {
                            s = 0;
                            int sb = 0;
                            for (int i = 1; (scan >= lastscan + i) && (pos >= i); i++)
                            {
                                if (oldData[pos - i] == newData[scan - i])
                                    s++;
                                if (s * 2 - i > sb * 2 - lenb)
                                {
                                    sb = s;
                                    lenb = i;
                                }
                            }
                        }

                        if (lastscan + lenf > scan - lenb)
                        {
                            int overlap = (lastscan + lenf) - (scan - lenb);
                            s = 0;
                            int ss = 0;
                            int lens = 0;
                            for (int i = 0; i < overlap; i++)
                            {
                                if (newData[lastscan + lenf - overlap + i] == oldData[lastpos + lenf - overlap + i])
                                    s++;
                                if (newData[scan - lenb + i] == oldData[pos - lenb + i])
                                    s--;
                                if (s > ss)
                                {
                                    ss = s;
                                    lens = i + 1;
                                }
                            }

                            lenf += lens - overlap;
                            lenb -= lens;
                        }

                        for (int i = 0; i < lenf; i++)
                            db[dblen + i] = (byte)(newData[lastscan + i] - oldData[lastpos + i]);
                        for (int i = 0; i < (scan - lenb) - (lastscan + lenf); i++)
                            eb[eblen + i] = newData[lastscan + lenf + i];

                        dblen += lenf;
                        eblen += (scan - lenb) - (lastscan + lenf);

                        byte[] buf = new byte[8];
                        WriteInt64(lenf, buf, 0);
                        bz2Stream.Write(buf, 0, 8);

                        WriteInt64((scan - lenb) - (lastscan + lenf), buf, 0);
                        bz2Stream.Write(buf, 0, 8);

                        WriteInt64((pos - lenb) - (lastpos + lenf), buf, 0);
                        bz2Stream.Write(buf, 0, 8);

                        lastscan = scan - lenb;
                        lastpos = pos - lenb;
                        lastoffset = pos - scan;
                    }
                }
            }

            // compute size of compressed ctrl data
            long controlEndPosition = output.Position;
            WriteInt64(controlEndPosition - startPosition - c_headerSize, header, 8);

            // write compressed diff data
            using (BZip2OutputStream bz2Stream = new BZip2OutputStream(new WrappingStream(output, Ownership.None)))
            {
                bz2Stream.Write(db, 0, dblen);
            }

            // compute size of compressed diff data
            long diffEndPosition = output.Position;
            WriteInt64(diffEndPosition - controlEndPosition, header, 16);

            // write compressed extra data
            using (BZip2OutputStream bz2Stream = new BZip2OutputStream(new WrappingStream(output, Ownership.None)))
            {
                bz2Stream.Write(eb, 0, eblen);
            }

            // seek to the beginning, write the header, then seek back to end
            long endPosition = output.Position;
            output.Position = startPosition;
            output.Write(header, 0, header.Length);
            output.Position = endPosition;
        }

        /// <summary>
        /// Applies a binary patch (in <a href="http://www.daemonology.net/bsdiff/">bsdiff</a> format) to the data in
        /// <paramref name="input"/> and writes the results of patching to <paramref name="output"/>.
        /// </summary>
        /// <param name="input">A <see cref="Stream"/> containing the input data.</param>
        /// <param name="openPatchStream">A func that can open a <see cref="Stream"/> positioned at the start of the patch data.
        /// This stream must support reading and seeking, and <paramref name="openPatchStream"/> must allow multiple streams on
        /// the patch to be opened concurrently.</param>
        /// <param name="output">A <see cref="Stream"/> to which the patched data is written.</param>
        public static void Apply(Stream input, Func<Stream> openPatchStream, Stream output)
        {
            // check arguments
            if (input == null)
                throw new ArgumentNullException("input");
            if (openPatchStream == null)
                throw new ArgumentNullException("openPatchStream");
            if (output == null)
                throw new ArgumentNullException("output");

            /*
            File format:
                0   8   "BSDIFF40"
                8   8   X
                16  8   Y
                24  8   sizeof(newfile)
                32  X   bzip2(control block)
                32+X    Y   bzip2(diff block)
                32+X+Y  ??? bzip2(extra block)
            with control block a set of triples (x,y,z) meaning "add x bytes
            from oldfile to x bytes from the diff block; copy y bytes from the
            extra block; seek forwards in oldfile by z bytes".
            */
            // read header
            long controlLength, diffLength, newSize;
            using (Stream patchStream = openPatchStream())
            {
                // check patch stream capabilities
                if (!patchStream.CanRead)
                    throw new ArgumentException("Patch stream must be readable.", "openPatchStream");
                if (!patchStream.CanSeek)
                    throw new ArgumentException("Patch stream must be seekable.", "openPatchStream");

                byte[] header = patchStream.ReadExactly(c_headerSize);

                // check for appropriate magic
                long signature = ReadInt64(header, 0);
                if (signature != c_fileSignature)
                    throw new InvalidOperationException("Corrupt patch.");

                // read lengths from header
                controlLength = ReadInt64(header, 8);
                diffLength = ReadInt64(header, 16);
                newSize = ReadInt64(header, 24);
                if (controlLength < 0 || diffLength < 0 || newSize < 0)
                    throw new InvalidOperationException("Corrupt patch.");
            }

            // preallocate buffers for reading and writing
            const int c_bufferSize = 1048576;
            byte[] newData = new byte[c_bufferSize];
            byte[] oldData = new byte[c_bufferSize];

            // prepare to read three parts of the patch in parallel
            using (Stream compressedControlStream = openPatchStream())
            using (Stream compressedDiffStream = openPatchStream())
            using (Stream compressedExtraStream = openPatchStream())
            {
                // seek to the start of each part
                compressedControlStream.Seek(c_headerSize, SeekOrigin.Current);
                compressedDiffStream.Seek(c_headerSize + controlLength, SeekOrigin.Current);
                compressedExtraStream.Seek(c_headerSize + controlLength + diffLength, SeekOrigin.Current);

                // decompress each part (to read it)
                using (BZip2InputStream controlStream = new BZip2InputStream(compressedControlStream))
                using (BZip2InputStream diffStream = new BZip2InputStream(compressedDiffStream))
                using (BZip2InputStream extraStream = new BZip2InputStream(compressedExtraStream))
                {
                    long[] control = new long[3];
                    byte[] buffer = new byte[8];

                    int oldPosition = 0;
                    int newPosition = 0;
                    while (newPosition < newSize)
                    {
                        // read control data
                        for (int i = 0; i < 3; i++)
                        {
                            controlStream.ReadExactly(buffer, 0, 8);
                            control[i] = ReadInt64(buffer, 0);
                        }

                        // sanity-check
                        if (newPosition + control[0] > newSize)
                            throw new InvalidOperationException("Corrupt patch.");

                        // seek old file to the position that the new data is diffed against
                        input.Position = oldPosition;

                        int bytesToCopy = (int)control[0];
                        while (bytesToCopy > 0)
                        {
                            int actualBytesToCopy = Math.Min(bytesToCopy, c_bufferSize);

                            // read diff string
                            diffStream.ReadExactly(newData, 0, actualBytesToCopy);

                            // add old data to diff string
                            int availableInputBytes = Math.Min(actualBytesToCopy, (int)(input.Length - input.Position));
                            input.ReadExactly(oldData, 0, availableInputBytes);

                            for (int index = 0; index < availableInputBytes; index++)
                                newData[index] += oldData[index];

                            output.Write(newData, 0, actualBytesToCopy);

                            // adjust counters
                            newPosition += actualBytesToCopy;
                            oldPosition += actualBytesToCopy;
                            bytesToCopy -= actualBytesToCopy;
                        }

                        // sanity-check
                        if (newPosition + control[1] > newSize)
                            throw new InvalidOperationException("Corrupt patch.");

                        // read extra string
                        bytesToCopy = (int)control[1];
                        while (bytesToCopy > 0)
                        {
                            int actualBytesToCopy = Math.Min(bytesToCopy, c_bufferSize);

                            extraStream.ReadExactly(newData, 0, actualBytesToCopy);
                            output.Write(newData, 0, actualBytesToCopy);

                            newPosition += actualBytesToCopy;
                            bytesToCopy -= actualBytesToCopy;
                        }

                        // adjust position
                        oldPosition = (int)(oldPosition + control[2]);
                    }
                }
            }
        }

        private static int CompareBytes(byte[] left, int leftOffset, byte[] right, int rightOffset)
        {
            for (int index = 0; index < left.Length - leftOffset && index < right.Length - rightOffset; index++)
            {
                int diff = left[index + leftOffset] - right[index + rightOffset];
                if (diff != 0)
                    return diff;
            }
            return 0;
        }

        private static int MatchLength(byte[] oldData, int oldOffset, byte[] newData, int newOffset)
        {
            int i;
            for (i = 0; i < oldData.Length - oldOffset && i < newData.Length - newOffset; i++)
            {
                if (oldData[i + oldOffset] != newData[i + newOffset])
                    break;
            }
            return i;
        }

        private static int Search(int[] I, byte[] oldData, byte[] newData, int newOffset, int start, int end, out int pos)
        {
            if (end - start < 2)
            {
                int startLength = MatchLength(oldData, I[start], newData, newOffset);
                int endLength = MatchLength(oldData, I[end], newData, newOffset);

                if (startLength > endLength)
                {
                    pos = I[start];
                    return startLength;
                }
                else
                {
                    pos = I[end];
                    return endLength;
                }
            }
            else
            {
                int midPoint = start + (end - start) / 2;
                return CompareBytes(oldData, I[midPoint], newData, newOffset) < 0 ?
                    Search(I, oldData, newData, newOffset, midPoint, end, out pos) :
                    Search(I, oldData, newData, newOffset, start, midPoint, out pos);
            }
        }

        private static void Split(int[] I, int[] v, int start, int len, int h)
        {
            if (len < 16)
            {
                int j;
                for (int k = start; k < start + len; k += j)
                {
                    j = 1;
                    int x = v[I[k] + h];
                    for (int i = 1; k + i < start + len; i++)
                    {
                        if (v[I[k + i] + h] < x)
                        {
                            x = v[I[k + i] + h];
                            j = 0;
                        }
                        if (v[I[k + i] + h] == x)
                        {
                            Swap(ref I[k + j], ref I[k + i]);
                            j++;
                        }
                    }
                    for (int i = 0; i < j; i++)
                        v[I[k + i]] = k + j - 1;
                    if (j == 1)
                        I[k] = -1;
                }
            }
            else
            {
                int x = v[I[start + len / 2] + h];
                int jj = 0;
                int kk = 0;
                for (int i2 = start; i2 < start + len; i2++)
                {
                    if (v[I[i2] + h] < x)
                        jj++;
                    if (v[I[i2] + h] == x)
                        kk++;
                }
                jj += start;
                kk += jj;

                int i = start;
                int j = 0;
                int k = 0;
                while (i < jj)
                {
                    if (v[I[i] + h] < x)
                    {
                        i++;
                    }
                    else if (v[I[i] + h] == x)
                    {
                        Swap(ref I[i], ref I[jj + j]);
                        j++;
                    }
                    else
                    {
                        Swap(ref I[i], ref I[kk + k]);
                        k++;
                    }
                }

                while (jj + j < kk)
                {
                    if (v[I[jj + j] + h] == x)
                    {
                        j++;
                    }
                    else
                    {
                        Swap(ref I[jj + j], ref I[kk + k]);
                        k++;
                    }
                }

                if (jj > start)
                    Split(I, v, start, jj - start, h);

                for (i = 0; i < kk - jj; i++)
                    v[I[jj + i]] = kk - 1;
                if (jj == kk - 1)
                    I[jj] = -1;

                if (start + len > kk)
                    Split(I, v, kk, start + len - kk, h);
            }
        }

        private static int[] SuffixSort(byte[] oldData)
        {
            int[] buckets = new int[256];

            foreach (byte oldByte in oldData)
                buckets[oldByte]++;
            for (int i = 1; i < 256; i++)
                buckets[i] += buckets[i - 1];
            for (int i = 255; i > 0; i--)
                buckets[i] = buckets[i - 1];
            buckets[0] = 0;

            int[] I = new int[oldData.Length + 1];
            for (int i = 0; i < oldData.Length; i++)
                I[++buckets[oldData[i]]] = i;

            int[] v = new int[oldData.Length + 1];
            for (int i = 0; i < oldData.Length; i++)
                v[i] = buckets[oldData[i]];

            for (int i = 1; i < 256; i++)
            {
                if (buckets[i] == buckets[i - 1] + 1)
                    I[buckets[i]] = -1;
            }
            I[0] = -1;

            for (int h = 1; I[0] != -(oldData.Length + 1); h += h)
            {
                int len = 0;
                int i = 0;
                while (i < oldData.Length + 1)
                {
                    if (I[i] < 0)
                    {
                        len -= I[i];
                        i -= I[i];
                    }
                    else
                    {
                        if (len != 0)
                            I[i - len] = -len;
                        len = v[I[i]] + 1 - i;
                        Split(I, v, i, len, h);
                        i += len;
                        len = 0;
                    }
                }

                if (len != 0)
                    I[i - len] = -len;
            }

            for (int i = 0; i < oldData.Length + 1; i++)
                I[v[i]] = i;

            return I;
        }

        private static void Swap(ref int first, ref int second)
        {
            int temp = first;
            first = second;
            second = temp;
        }

        private static long ReadInt64(byte[] buf, int offset)
        {
            long value = buf[offset + 7] & 0x7F;

            for (int index = 6; index >= 0; index--)
            {
                value *= 256;
                value += buf[offset + index];
            }

            if ((buf[offset + 7] & 0x80) != 0)
                value = -value;

            return value;
        }

        private static void WriteInt64(long value, byte[] buf, int offset)
        {
            long valueToWrite = value < 0 ? -value : value;

            for (int byteIndex = 0; byteIndex < 8; byteIndex++)
            {
                buf[offset + byteIndex] = (byte)(valueToWrite % 256);
                valueToWrite -= buf[offset + byteIndex];
                valueToWrite /= 256;
            }

            if (value < 0)
                buf[offset + 7] |= 0x80;
        }

        const long c_fileSignature = 0x3034464649445342L;
        const int c_headerSize = 32;
    }

    /// <summary>
    /// A <see cref="Stream"/> that wraps another stream. One major feature of <see cref="WrappingStream"/> is that it does not dispose the
    /// underlying stream when it is disposed if Ownership.None is used; this is useful when using classes such as <see cref="BinaryReader"/> and
    /// <see cref="System.Security.Cryptography.CryptoStream"/> that take ownership of the stream passed to their constructors.
    /// </summary>
    /// <remarks>See <a href="http://code.logos.com/blog/2009/05/wrappingstream_implementation.html">WrappingStream Implementation</a>.</remarks>
    public class WrappingStream : Stream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WrappingStream"/> class.
        /// </summary>
        /// <param name="streamBase">The wrapped stream.</param>
        /// <param name="ownership">Use Owns if the wrapped stream should be disposed when this stream is disposed.</param>
        public WrappingStream(Stream streamBase, Ownership ownership)
        {
            // check parameters
            if (streamBase == null)
                throw new ArgumentNullException("streamBase");

            m_streamBase = streamBase;
            m_ownership = ownership;
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading.
        /// </summary>
        /// <returns><c>true</c> if the stream supports reading; otherwise, <c>false</c>.</returns>
        public override bool CanRead
        {
            get { return m_streamBase == null ? false : m_streamBase.CanRead; }
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports seeking.
        /// </summary>
        /// <returns><c>true</c> if the stream supports seeking; otherwise, <c>false</c>.</returns>
        public override bool CanSeek
        {
            get { return m_streamBase == null ? false : m_streamBase.CanSeek; }
        }

        /// <summary>
        /// Gets a value indicating whether the current stream supports writing.
        /// </summary>
        /// <returns><c>true</c> if the stream supports writing; otherwise, <c>false</c>.</returns>
        public override bool CanWrite
        {
            get { return m_streamBase == null ? false : m_streamBase.CanWrite; }
        }

        /// <summary>
        /// Gets the length in bytes of the stream.
        /// </summary>
        public override long Length
        {
            get { ThrowIfDisposed(); return m_streamBase.Length; }
        }

        /// <summary>
        /// Gets or sets the position within the current stream.
        /// </summary>
        public override long Position
        {
            get { ThrowIfDisposed(); return m_streamBase.Position; }
            set { ThrowIfDisposed(); m_streamBase.Position = value; }
        }

        /// <summary>
        /// Begins an asynchronous read operation.
        /// </summary>
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ThrowIfDisposed();
            return m_streamBase.BeginRead(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Begins an asynchronous write operation.
        /// </summary>
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            ThrowIfDisposed();
            return m_streamBase.BeginWrite(buffer, offset, count, callback, state);
        }

        /// <summary>
        /// Waits for the pending asynchronous read to complete.
        /// </summary>
        public override int EndRead(IAsyncResult asyncResult)
        {
            ThrowIfDisposed();
            return m_streamBase.EndRead(asyncResult);
        }

        /// <summary>
        /// Ends an asynchronous write operation.
        /// </summary>
        public override void EndWrite(IAsyncResult asyncResult)
        {
            ThrowIfDisposed();
            m_streamBase.EndWrite(asyncResult);
        }

        /// <summary>
        /// Clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        /// </summary>
        public override void Flush()
        {
            ThrowIfDisposed();
            m_streamBase.Flush();
        }

        /// <summary>
        /// Reads a sequence of bytes from the current stream and advances the position
        /// within the stream by the number of bytes read.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            return m_streamBase.Read(buffer, offset, count);
        }

        /// <summary>
        /// Reads a byte from the stream and advances the position within the stream by one byte, or returns -1 if at the end of the stream.
        /// </summary>
        public override int ReadByte()
        {
            ThrowIfDisposed();
            return m_streamBase.ReadByte();
        }

        /// <summary>
        /// Sets the position within the current stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
        /// <param name="origin">A value of type <see cref="T:System.IO.SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the current stream.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            return m_streamBase.Seek(offset, origin);
        }

        /// <summary>
        /// Sets the length of the current stream.
        /// </summary>
        /// <param name="value">The desired length of the current stream in bytes.</param>
        public override void SetLength(long value)
        {
            ThrowIfDisposed();
            m_streamBase.SetLength(value);
        }

        /// <summary>
        /// Writes a sequence of bytes to the current stream and advances the current position
        /// within this stream by the number of bytes written.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            m_streamBase.Write(buffer, offset, count);
        }

        /// <summary>
        /// Writes a byte to the current position in the stream and advances the position within the stream by one byte.
        /// </summary>
        public override void WriteByte(byte value)
        {
            ThrowIfDisposed();
            m_streamBase.WriteByte(value);
        }

        /// <summary>
        /// Gets the wrapped stream.
        /// </summary>
        /// <value>The wrapped stream.</value>
        protected Stream WrappedStream
        {
            get { return m_streamBase; }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="WrappingStream"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                // doesn't close the base stream, but just prevents access to it through this WrappingStream
                if (disposing)
                {
                    if (m_streamBase != null && m_ownership == Ownership.Owns)
                        m_streamBase.Dispose();
                    m_streamBase = null;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        private void ThrowIfDisposed()
        {
            // throws an ObjectDisposedException if this object has been disposed
            if (m_streamBase == null)
                throw new ObjectDisposedException(GetType().Name);
        }

        Stream m_streamBase;
        readonly Ownership m_ownership;
    }

    /// <summary>
    /// Indicates whether an object takes ownership of an item.
    /// </summary>
    public enum Ownership
    {
        /// <summary>
        /// The object does not own this item.
        /// </summary>
        None,

        /// <summary>
        /// The object owns this item, and is responsible for releasing it.
        /// </summary>
        Owns
    }

    /// <summary>
    /// Provides helper methods for working with <see cref="Stream"/>.
    /// </summary>
    public static class StreamUtility
    {
        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="count">The count of bytes to read.</param>
        /// <returns>A new byte array containing the data read from the stream.</returns>
        public static byte[] ReadExactly(this Stream stream, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            byte[] buffer = new byte[count];
            ReadExactly(stream, buffer, 0, count);
            return buffer;
        }

        /// <summary>
        /// Reads exactly <paramref name="count"/> bytes from <paramref name="stream"/> into
        /// <paramref name="buffer"/>, starting at the byte given by <paramref name="offset"/>.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to read data into.</param>
        /// <param name="offset">The offset within the buffer at which data is first written.</param>
        /// <param name="count">The count of bytes to read.</param>
        public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
        {
            // check arguments
            if (stream == null)
                throw new ArgumentNullException("stream");
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || buffer.Length - offset < count)
                throw new ArgumentOutOfRangeException("count");

            while (count > 0)
            {
                // read data
                int bytesRead = stream.Read(buffer, offset, count);

                // check for failure to read
                if (bytesRead == 0)
                    throw new EndOfStreamException();

                // move to next block
                offset += bytesRead;
                count -= bytesRead;
            }
        }
    }
}