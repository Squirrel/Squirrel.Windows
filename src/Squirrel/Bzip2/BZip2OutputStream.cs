//#define Trace

// BZip2OutputStream.cs
// ------------------------------------------------------------------
//
// Copyright (c) 2011 Dino Chiesa.
// All rights reserved.
//
// This code module is part of DotNetZip, a zipfile class library.
//
// ------------------------------------------------------------------
//
// This code is licensed under the Microsoft Public License.
// See the file License.txt for the license details.
// More info on: http://dotnetzip.codeplex.com
//
// ------------------------------------------------------------------
//
// Last Saved: <2011-August-02 16:44:11>
//
// ------------------------------------------------------------------
//
// This module defines the BZip2OutputStream class, which is a
// compressing stream that handles BZIP2. This code may have been
// derived in part from Apache commons source code. The license below
// applies to the original Apache code.
//
// ------------------------------------------------------------------
// flymake: csc.exe /t:module BZip2InputStream.cs BZip2Compressor.cs Rand.cs BCRC32.cs @@FILE@@

/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */


// Design Notes:
//
// This class follows the classic Decorator pattern: it is a Stream that
// wraps itself around a Stream, and in doing so provides bzip2
// compression as callers Write into it.
//
// BZip2 is a straightforward data format: there are 4 magic bytes at
// the top of the file, followed by 1 or more compressed blocks. There
// is a small "magic byte" trailer after all compressed blocks. This
// class emits the magic bytes for the header and trailer, and relies on
// a BZip2Compressor to generate each of the compressed data blocks.
//
// BZip2 does byte-shredding - it uses partial fractions of bytes to
// represent independent pieces of information. This class relies on the
// BitWriter to adapt the bit-oriented BZip2 output to the byte-oriented
// model of the .NET Stream class.
//
// ----
//
// Regarding the Apache code base: Most of the code in this particular
// class is related to stream operations, and is my own code. It largely
// does not rely on any code obtained from Apache commons. If you
// compare this code with the Apache commons BZip2OutputStream, you will
// see very little code that is common, except for the
// nearly-boilerplate structure that is common to all subtypes of
// System.IO.Stream. There may be some small remnants of code in this
// module derived from the Apache stuff, which is why I left the license
// in here. Most of the Apache commons compressor magic has been ported
// into the BZip2Compressor class.
//

using System;
using System.IO;


namespace Ionic.BZip2
{
    /// <summary>
    ///   A write-only decorator stream that compresses data as it is
    ///   written using the BZip2 algorithm.
    /// </summary>
    public class BZip2OutputStream : System.IO.Stream
    {
        int totalBytesWrittenIn;
        bool leaveOpen;
        BZip2Compressor compressor;
        uint combinedCRC;
        Stream output;
        BitWriter bw;
        int blockSize100k;  // 0...9

        private TraceBits desiredTrace = TraceBits.Crc | TraceBits.Write;

        /// <summary>
        ///   Constructs a new <c>BZip2OutputStream</c>, that sends its
        ///   compressed output to the given output stream.
        /// </summary>
        ///
        /// <param name='output'>
        ///   The destination stream, to which compressed output will be sent.
        /// </param>
        ///
        /// <example>
        ///
        ///   This example reads a file, then compresses it with bzip2 file,
        ///   and writes the compressed data into a newly created file.
        ///
        ///   <code>
        ///   var fname = "logfile.log";
        ///   using (var fs = File.OpenRead(fname))
        ///   {
        ///       var outFname = fname + ".bz2";
        ///       using (var output = File.Create(outFname))
        ///       {
        ///           using (var compressor = new Ionic.BZip2.BZip2OutputStream(output))
        ///           {
        ///               byte[] buffer = new byte[2048];
        ///               int n;
        ///               while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        ///               {
        ///                   compressor.Write(buffer, 0, n);
        ///               }
        ///           }
        ///       }
        ///   }
        ///   </code>
        /// </example>
        public BZip2OutputStream(Stream output)
            : this(output, BZip2.MaxBlockSize, false)
        {
        }


        /// <summary>
        ///   Constructs a new <c>BZip2OutputStream</c> with specified blocksize.
        /// </summary>
        /// <param name = "output">the destination stream.</param>
        /// <param name = "blockSize">
        ///   The blockSize in units of 100000 bytes.
        ///   The valid range is 1..9.
        /// </param>
        public BZip2OutputStream(Stream output, int blockSize)
            : this(output, blockSize, false)
        {
        }


        /// <summary>
        ///   Constructs a new <c>BZip2OutputStream</c>.
        /// </summary>
        ///   <param name = "output">the destination stream.</param>
        /// <param name = "leaveOpen">
        ///   whether to leave the captive stream open upon closing this stream.
        /// </param>
        public BZip2OutputStream(Stream output, bool leaveOpen)
            : this(output, BZip2.MaxBlockSize, leaveOpen)
        {
        }


        /// <summary>
        ///   Constructs a new <c>BZip2OutputStream</c> with specified blocksize,
        ///   and explicitly specifies whether to leave the wrapped stream open.
        /// </summary>
        ///
        /// <param name = "output">the destination stream.</param>
        /// <param name = "blockSize">
        ///   The blockSize in units of 100000 bytes.
        ///   The valid range is 1..9.
        /// </param>
        /// <param name = "leaveOpen">
        ///   whether to leave the captive stream open upon closing this stream.
        /// </param>
        public BZip2OutputStream(Stream output, int blockSize, bool leaveOpen)
        {
            if (blockSize < BZip2.MinBlockSize ||
                blockSize > BZip2.MaxBlockSize)
            {
                var msg = String.Format("blockSize={0} is out of range; must be between {1} and {2}",
                                        blockSize,
                                        BZip2.MinBlockSize, BZip2.MaxBlockSize);
                throw new ArgumentException(msg, "blockSize");
            }

            this.output = output;
            if (!this.output.CanWrite)
                throw new ArgumentException("The stream is not writable.", "output");

            this.bw = new BitWriter(this.output);
            this.blockSize100k = blockSize;
            this.compressor = new BZip2Compressor(this.bw, blockSize);
            this.leaveOpen = leaveOpen;
            this.combinedCRC = 0;
            EmitHeader();
        }




        /// <summary>
        ///   Close the stream.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This may or may not close the underlying stream.  Check the
        ///     constructors that accept a bool value.
        ///   </para>
        /// </remarks>
        public override void Close()
        {
            if (output != null)
            {
                Stream o = this.output;
                Finish();
                if (!leaveOpen)
                    o.Close();
            }
        }


        /// <summary>
        ///   Flush the stream.
        /// </summary>
        public override void Flush()
        {
            if (this.output != null)
            {
                this.bw.Flush();
                this.output.Flush();
            }
        }

        private void EmitHeader()
        {
            var magic = new byte[] {
                (byte) 'B',
                (byte) 'Z',
                (byte) 'h',
                (byte) ('0' + this.blockSize100k)
            };

            // not necessary to shred the initial magic bytes
            this.output.Write(magic, 0, magic.Length);
        }

        private void EmitTrailer()
        {
            // A magic 48-bit number, 0x177245385090, to indicate the end
            // of the last block. (sqrt(pi), if you want to know)

            TraceOutput(TraceBits.Write, "total written out: {0} (0x{0:X})",
                        this.bw.TotalBytesWrittenOut);

            // must shred
            this.bw.WriteByte(0x17);
            this.bw.WriteByte(0x72);
            this.bw.WriteByte(0x45);
            this.bw.WriteByte(0x38);
            this.bw.WriteByte(0x50);
            this.bw.WriteByte(0x90);

            this.bw.WriteInt(this.combinedCRC);

            this.bw.FinishAndPad();

            TraceOutput(TraceBits.Write, "final total: {0} (0x{0:X})",
                        this.bw.TotalBytesWrittenOut);
        }

        void Finish()
        {
            // Console.WriteLine("BZip2:Finish");

            try
            {
                var totalBefore = this.bw.TotalBytesWrittenOut;
                this.compressor.CompressAndWrite();
                TraceOutput(TraceBits.Write,"out block length (bytes): {0} (0x{0:X})",
                            this.bw.TotalBytesWrittenOut - totalBefore);

                TraceOutput(TraceBits.Crc, " combined CRC (before): {0:X8}",
                            this.combinedCRC);
                this.combinedCRC = (this.combinedCRC << 1) | (this.combinedCRC >> 31);
                this.combinedCRC ^= (uint) compressor.Crc32;
                TraceOutput(TraceBits.Crc, " block    CRC         : {0:X8}",
                            this.compressor.Crc32);
                TraceOutput(TraceBits.Crc, " combined CRC (final) : {0:X8}",
                            this.combinedCRC);

                EmitTrailer();
            }
            finally
            {
                this.output = null;
                this.compressor = null;
                this.bw = null;
            }
        }


        /// <summary>
        ///   The blocksize parameter specified at construction time.
        /// </summary>
        public int BlockSize
        {
            get { return this.blockSize100k; }
        }


        /// <summary>
        ///   Write data to the stream.
        /// </summary>
        /// <remarks>
        ///
        /// <para>
        ///   Use the <c>BZip2OutputStream</c> to compress data while writing:
        ///   create a <c>BZip2OutputStream</c> with a writable output stream.
        ///   Then call <c>Write()</c> on that <c>BZip2OutputStream</c>, providing
        ///   uncompressed data as input.  The data sent to the output stream will
        ///   be the compressed form of the input data.
        /// </para>
        ///
        /// <para>
        ///   A <c>BZip2OutputStream</c> can be used only for <c>Write()</c> not for <c>Read()</c>.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <param name="buffer">The buffer holding data to write to the stream.</param>
        /// <param name="offset">the offset within that data array to find the first byte to write.</param>
        /// <param name="count">the number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (offset < 0)
                throw new IndexOutOfRangeException(String.Format("offset ({0}) must be > 0", offset));
            if (count < 0)
                throw new IndexOutOfRangeException(String.Format("count ({0}) must be > 0", count));
            if (offset + count > buffer.Length)
                throw new IndexOutOfRangeException(String.Format("offset({0}) count({1}) bLength({2})",
                                                                 offset, count, buffer.Length));
            if (this.output == null)
                throw new IOException("the stream is not open");

            if (count == 0) return;  // nothing to do

            int bytesWritten = 0;
            int bytesRemaining = count;

            do
            {
                int n = compressor.Fill(buffer, offset, bytesRemaining);
                if (n != bytesRemaining)
                {
                    // The compressor data block is full.  Compress and
                    // write out the compressed data, then reset the
                    // compressor and continue.

                    var totalBefore = this.bw.TotalBytesWrittenOut;
                    this.compressor.CompressAndWrite();
                    TraceOutput(TraceBits.Write,"out block length (bytes): {0} (0x{0:X})",
                                this.bw.TotalBytesWrittenOut - totalBefore);

                            // and now any remaining bits
                            TraceOutput(TraceBits.Write,
                                        " remaining: {0} 0x{1:X}",
                                        this.bw.NumRemainingBits,
                                        this.bw.RemainingBits);

                    TraceOutput(TraceBits.Crc, " combined CRC (before): {0:X8}",
                                this.combinedCRC);
                    this.combinedCRC = (this.combinedCRC << 1) | (this.combinedCRC >> 31);
                    this.combinedCRC ^= (uint) compressor.Crc32;
                    TraceOutput(TraceBits.Crc, " block    CRC         : {0:X8}",
                                compressor.Crc32);
                    TraceOutput(TraceBits.Crc, " combined CRC (after) : {0:X8}",
                                this.combinedCRC);
                    offset += n;
                }
                bytesRemaining -= n;
                bytesWritten += n;
            } while (bytesRemaining > 0);

            totalBytesWrittenIn += bytesWritten;
        }




        /// <summary>
        /// Indicates whether the stream can be read.
        /// </summary>
        /// <remarks>
        /// The return value is always false.
        /// </remarks>
        public override bool CanRead
        {
            get { return false; }
        }

        /// <summary>
        /// Indicates whether the stream supports Seek operations.
        /// </summary>
        /// <remarks>
        /// Always returns false.
        /// </remarks>
        public override bool CanSeek
        {
            get { return false; }
        }

        /// <summary>
        /// Indicates whether the stream can be written.
        /// </summary>
        /// <remarks>
        /// The return value should always be true, unless and until the
        /// object is disposed and closed.
        /// </remarks>
        public override bool CanWrite
        {
            get
            {
                if (this.output == null) throw new ObjectDisposedException("BZip2Stream");
                return this.output.CanWrite;
            }
        }

        /// <summary>
        /// Reading this property always throws a <see cref="NotImplementedException"/>.
        /// </summary>
        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>
        /// The position of the stream pointer.
        /// </summary>
        ///
        /// <remarks>
        ///   Setting this property always throws a <see
        ///   cref="NotImplementedException"/>. Reading will return the
        ///   total number of uncompressed bytes written through.
        /// </remarks>
        public override long Position
        {
            get
            {
                return this.totalBytesWrittenIn;
            }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        /// Calling this method always throws a <see cref="NotImplementedException"/>.
        /// </summary>
        /// <param name="offset">this is irrelevant, since it will always throw!</param>
        /// <param name="origin">this is irrelevant, since it will always throw!</param>
        /// <returns>irrelevant!</returns>
        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Calling this method always throws a <see cref="NotImplementedException"/>.
        /// </summary>
        /// <param name="value">this is irrelevant, since it will always throw!</param>
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        ///   Calling this method always throws a <see cref="NotImplementedException"/>.
        /// </summary>
        /// <param name='buffer'>this parameter is never used</param>
        /// <param name='offset'>this parameter is never used</param>
        /// <param name='count'>this parameter is never used</param>
        /// <returns>never returns anything; always throws</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }


        // used only when Trace is defined
        [Flags]
        enum TraceBits : uint
        {
            None         = 0,
            Crc          = 1,
            Write        = 2,
            All          = 0xffffffff,
        }


        [System.Diagnostics.ConditionalAttribute("Trace")]
        private void TraceOutput(TraceBits bits, string format, params object[] varParams)
        {
            if ((bits & this.desiredTrace) != 0)
            {
                //lock(outputLock)
                {
                    int tid = System.Threading.Thread.CurrentThread.GetHashCode();
#if !SILVERLIGHT && !NETCF
                    Console.ForegroundColor = (ConsoleColor) (tid % 8 + 10);
#endif
                    Console.Write("{0:000} PBOS ", tid);
                    Console.WriteLine(format, varParams);
#if !SILVERLIGHT && !NETCF
                    Console.ResetColor();
#endif
                }
            }
        }


    }

}
