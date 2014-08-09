// BZip2InputStream.cs
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
// Last Saved: <2011-July-31 11:57:32>
//
// ------------------------------------------------------------------
//
// This module defines the BZip2InputStream class, which is a decompressing
// stream that handles BZIP2. This code is derived from Apache commons source code.
// The license below applies to the original Apache code.
//
// ------------------------------------------------------------------

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

/*
 * This package is based on the work done by Keiron Liddle, Aftex Software
 * <keiron@aftexsw.com> to whom the Ant project is very grateful for his
 * great code.
 */

// compile: msbuild
// not: csc.exe /t:library /debug+ /out:Ionic.BZip2.dll BZip2InputStream.cs BCRC32.cs Rand.cs



using System;
using System.IO;

namespace Ionic.BZip2
{

    /// <summary>
    ///   A read-only decorator stream that performs BZip2 decompression on Read.
    /// </summary>
    public class BZip2InputStream : System.IO.Stream
    {
        bool _disposed;
        bool _leaveOpen;
        Int64 totalBytesRead;
        private int last;

        /* for undoing the Burrows-Wheeler transform */
        private int origPtr;

        // blockSize100k: 0 .. 9.
        //
        // This var name is a misnomer. The actual block size is 100000
        // * blockSize100k. (not 100k * blocksize100k)
        private int blockSize100k;
        private bool blockRandomised;
        private int bsBuff;
        private int bsLive;
        private readonly Ionic.Crc.CRC32 crc = new Ionic.Crc.CRC32(true);
        private int nInUse;
        private Stream input;
        private int currentChar = -1;

        /// <summary>
        ///   Compressor State
        /// </summary>
        enum CState
        {
            EOF = 0,
            START_BLOCK = 1,
            RAND_PART_A = 2,
            RAND_PART_B = 3,
            RAND_PART_C = 4,
            NO_RAND_PART_A = 5,
            NO_RAND_PART_B = 6,
            NO_RAND_PART_C = 7,
        }

        private CState currentState = CState.START_BLOCK;

        private uint storedBlockCRC, storedCombinedCRC;
        private uint computedBlockCRC, computedCombinedCRC;

        // Variables used by setup* methods exclusively
        private int su_count;
        private int su_ch2;
        private int su_chPrev;
        private int su_i2;
        private int su_j2;
        private int su_rNToGo;
        private int su_rTPos;
        private int su_tPos;
        private char su_z;
        private BZip2InputStream.DecompressionState data;


        /// <summary>
        ///   Create a BZip2InputStream, wrapping it around the given input Stream.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The input stream will be closed when the BZip2InputStream is closed.
        ///   </para>
        /// </remarks>
        /// <param name='input'>The stream from which to read compressed data</param>
        public BZip2InputStream(Stream input)
            : this(input, false)
        {}


        /// <summary>
        ///   Create a BZip2InputStream with the given stream, and
        ///   specifying whether to leave the wrapped stream open when
        ///   the BZip2InputStream is closed.
        /// </summary>
        /// <param name='input'>The stream from which to read compressed data</param>
        /// <param name='leaveOpen'>
        ///   Whether to leave the input stream open, when the BZip2InputStream closes.
        /// </param>
        ///
        /// <example>
        ///
        ///   This example reads a bzip2-compressed file, decompresses it,
        ///   and writes the decompressed data into a newly created file.
        ///
        ///   <code>
        ///   var fname = "logfile.log.bz2";
        ///   using (var fs = File.OpenRead(fname))
        ///   {
        ///       using (var decompressor = new Ionic.BZip2.BZip2InputStream(fs))
        ///       {
        ///           var outFname = fname + ".decompressed";
        ///           using (var output = File.Create(outFname))
        ///           {
        ///               byte[] buffer = new byte[2048];
        ///               int n;
        ///               while ((n = decompressor.Read(buffer, 0, buffer.Length)) > 0)
        ///               {
        ///                   output.Write(buffer, 0, n);
        ///               }
        ///           }
        ///       }
        ///   }
        ///   </code>
        /// </example>
        public BZip2InputStream(Stream input, bool leaveOpen)
            : base()
        {

            this.input = input;
            this._leaveOpen = leaveOpen;
            init();
        }

        /// <summary>
        ///   Read data from the stream.
        /// </summary>
        ///
        /// <remarks>
        ///   <para>
        ///     To decompress a BZip2 data stream, create a <c>BZip2InputStream</c>,
        ///     providing a stream that reads compressed data.  Then call Read() on
        ///     that <c>BZip2InputStream</c>, and the data read will be decompressed
        ///     as you read.
        ///   </para>
        ///
        ///   <para>
        ///     A <c>BZip2InputStream</c> can be used only for <c>Read()</c>, not for <c>Write()</c>.
        ///   </para>
        /// </remarks>
        ///
        /// <param name="buffer">The buffer into which the read data should be placed.</param>
        /// <param name="offset">the offset within that data array to put the first byte read.</param>
        /// <param name="count">the number of bytes to read.</param>
        /// <returns>the number of bytes actually read</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset < 0)
                throw new IndexOutOfRangeException(String.Format("offset ({0}) must be > 0", offset));

            if (count < 0)
                throw new IndexOutOfRangeException(String.Format("count ({0}) must be > 0", count));

            if (offset + count > buffer.Length)
                throw new IndexOutOfRangeException(String.Format("offset({0}) count({1}) bLength({2})",
                                                                 offset, count, buffer.Length));

            if (this.input == null)
                throw new IOException("the stream is not open");


            int hi = offset + count;
            int destOffset = offset;
            for (int b; (destOffset < hi) && ((b = ReadByte()) >= 0);)
            {
                buffer[destOffset++] = (byte) b;
            }

            return (destOffset == offset) ? -1 : (destOffset - offset);
        }

        private void MakeMaps()
        {
            bool[] inUse = this.data.inUse;
            byte[] seqToUnseq = this.data.seqToUnseq;

            int n = 0;

            for (int i = 0; i < 256; i++)
            {
                if (inUse[i])
                    seqToUnseq[n++] = (byte) i;
            }

            this.nInUse = n;
        }

        /// <summary>
        ///   Read a single byte from the stream.
        /// </summary>
        /// <returns>the byte read from the stream, or -1 if EOF</returns>
        public override int ReadByte()
        {
            int retChar = this.currentChar;
            totalBytesRead++;
            switch (this.currentState)
            {
                case CState.EOF:
                    return -1;

                case CState.START_BLOCK:
                    throw new IOException("bad state");

                case CState.RAND_PART_A:
                    throw new IOException("bad state");

                case CState.RAND_PART_B:
                    SetupRandPartB();
                    break;

                case CState.RAND_PART_C:
                    SetupRandPartC();
                    break;

                case CState.NO_RAND_PART_A:
                    throw new IOException("bad state");

                case CState.NO_RAND_PART_B:
                    SetupNoRandPartB();
                    break;

                case CState.NO_RAND_PART_C:
                    SetupNoRandPartC();
                    break;

                default:
                    throw new IOException("bad state");
            }

            return retChar;
        }




        /// <summary>
        /// Indicates whether the stream can be read.
        /// </summary>
        /// <remarks>
        /// The return value depends on whether the captive stream supports reading.
        /// </remarks>
        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException("BZip2Stream");
                return this.input.CanRead;
            }
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
        /// The return value depends on whether the captive stream supports writing.
        /// </remarks>
        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException("BZip2Stream");
                return input.CanWrite;
            }
        }

        /// <summary>
        /// Flush the stream.
        /// </summary>
        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException("BZip2Stream");
            input.Flush();
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
        ///   total number of uncompressed bytes read in.
        /// </remarks>
        public override long Position
        {
            get
            {
                return this.totalBytesRead;
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
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }


        /// <summary>
        ///   Dispose the stream.
        /// </summary>
        /// <param name="disposing">
        ///   indicates whether the Dispose method was invoked by user code.
        /// </param>
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (!_disposed)
                {
                    if (disposing && (this.input != null))
                        this.input.Close();
                    _disposed = true;
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }


        void init()
        {
            if (null == this.input)
                throw new IOException("No input Stream");

            if (!this.input.CanRead)
                throw new IOException("Unreadable input Stream");

            CheckMagicChar('B', 0);
            CheckMagicChar('Z', 1);
            CheckMagicChar('h', 2);

            int blockSize = this.input.ReadByte();

            if ((blockSize < '1') || (blockSize > '9'))
                throw new IOException("Stream is not BZip2 formatted: illegal "
                                      + "blocksize " + (char) blockSize);

            this.blockSize100k = blockSize - '0';

            InitBlock();
            SetupBlock();
        }


        void CheckMagicChar(char expected, int position)
        {
            int magic = this.input.ReadByte();
            if (magic != (int)expected)
            {
                var msg = String.Format("Not a valid BZip2 stream. byte {0}, expected '{1}', got '{2}'",
                                        position, (int)expected, magic);
                throw new IOException(msg);
            }
        }


        void InitBlock()
        {
            char magic0 = bsGetUByte();
            char magic1 = bsGetUByte();
            char magic2 = bsGetUByte();
            char magic3 = bsGetUByte();
            char magic4 = bsGetUByte();
            char magic5 = bsGetUByte();

            if (magic0 == 0x17 && magic1 == 0x72 && magic2 == 0x45
                && magic3 == 0x38 && magic4 == 0x50 && magic5 == 0x90)
            {
                complete(); // end of file
            }
            else if (magic0 != 0x31 ||
                     magic1 != 0x41 ||
                     magic2 != 0x59 ||
                     magic3 != 0x26 ||
                     magic4 != 0x53 ||
                     magic5 != 0x59)
            {
                this.currentState = CState.EOF;
                var msg = String.Format("bad block header at offset 0x{0:X}",
                                      this.input.Position);
                throw new IOException(msg);
            }
            else
            {
                this.storedBlockCRC = bsGetInt();
                // Console.WriteLine(" stored block CRC     : {0:X8}", this.storedBlockCRC);

                this.blockRandomised = (GetBits(1) == 1);

                // Lazily allocate data
                if (this.data == null)
                    this.data = new DecompressionState(this.blockSize100k);

                // currBlockNo++;
                getAndMoveToFrontDecode();

                this.crc.Reset();
                this.currentState = CState.START_BLOCK;
            }
        }


        private void EndBlock()
        {
            this.computedBlockCRC = (uint)this.crc.Crc32Result;

            // A bad CRC is considered a fatal error.
            if (this.storedBlockCRC != this.computedBlockCRC)
            {
                // make next blocks readable without error
                // (repair feature, not yet documented, not tested)
                // this.computedCombinedCRC = (this.storedCombinedCRC << 1)
                //     | (this.storedCombinedCRC >> 31);
                // this.computedCombinedCRC ^= this.storedBlockCRC;

                var msg = String.Format("BZip2 CRC error (expected {0:X8}, computed {1:X8})",
                                        this.storedBlockCRC, this.computedBlockCRC);
                throw new IOException(msg);
            }

            // Console.WriteLine(" combined CRC (before): {0:X8}", this.computedCombinedCRC);
            this.computedCombinedCRC = (this.computedCombinedCRC << 1)
                | (this.computedCombinedCRC >> 31);
            this.computedCombinedCRC ^= this.computedBlockCRC;
            // Console.WriteLine(" computed block  CRC  : {0:X8}", this.computedBlockCRC);
            // Console.WriteLine(" combined CRC (after) : {0:X8}", this.computedCombinedCRC);
            // Console.WriteLine();
        }


        private void complete()
        {
            this.storedCombinedCRC = bsGetInt();
            this.currentState = CState.EOF;
            this.data = null;

            if (this.storedCombinedCRC != this.computedCombinedCRC)
            {
                var msg = String.Format("BZip2 CRC error (expected {0:X8}, computed {1:X8})",
                                      this.storedCombinedCRC, this.computedCombinedCRC);

                throw new IOException(msg);
            }
        }

        /// <summary>
        ///   Close the stream.
        /// </summary>
        public override void Close()
        {
            Stream inShadow = this.input;
            if (inShadow != null)
            {
                try
                {
                    if (!this._leaveOpen)
                        inShadow.Close();
                }
                finally
                {
                    this.data = null;
                    this.input = null;
                }
            }
        }


        /// <summary>
        ///   Read n bits from input, right justifying the result.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     For example, if you read 1 bit, the result is either 0
        ///     or 1.
        ///   </para>
        /// </remarks>
        /// <param name ="n">
        ///   The number of bits to read, always between 1 and 32.
        /// </param>
        private int GetBits(int n)
        {
            int bsLiveShadow = this.bsLive;
            int bsBuffShadow = this.bsBuff;

            if (bsLiveShadow < n)
            {
                do
                {
                    int thech = this.input.ReadByte();

                    if (thech < 0)
                        throw new IOException("unexpected end of stream");

                    // Console.WriteLine("R {0:X2}", thech);

                    bsBuffShadow = (bsBuffShadow << 8) | thech;
                    bsLiveShadow += 8;
                } while (bsLiveShadow < n);

                this.bsBuff = bsBuffShadow;
            }

            this.bsLive = bsLiveShadow - n;
            return (bsBuffShadow >> (bsLiveShadow - n)) & ((1 << n) - 1);
        }


        // private bool bsGetBit()
        // {
        //     int bsLiveShadow = this.bsLive;
        //     int bsBuffShadow = this.bsBuff;
        //
        //     if (bsLiveShadow < 1)
        //     {
        //         int thech = this.input.ReadByte();
        //
        //         if (thech < 0)
        //         {
        //             throw new IOException("unexpected end of stream");
        //         }
        //
        //         bsBuffShadow = (bsBuffShadow << 8) | thech;
        //         bsLiveShadow += 8;
        //         this.bsBuff = bsBuffShadow;
        //     }
        //
        //     this.bsLive = bsLiveShadow - 1;
        //     return ((bsBuffShadow >> (bsLiveShadow - 1)) & 1) != 0;
        // }

        private bool bsGetBit()
        {
            int bit = GetBits(1);
            return bit != 0;
        }

        private char bsGetUByte()
        {
            return (char) GetBits(8);
        }

        private uint bsGetInt()
        {
            return (uint)((((((GetBits(8) << 8) | GetBits(8)) << 8) | GetBits(8)) << 8) | GetBits(8));
        }


        /**
         * Called by createHuffmanDecodingTables() exclusively.
         */
        private static void hbCreateDecodeTables(int[] limit,
                                                 int[] bbase, int[] perm,  char[] length,
                                                 int minLen, int maxLen, int alphaSize)
        {
            for (int i = minLen, pp = 0; i <= maxLen; i++)
            {
                for (int j = 0; j < alphaSize; j++)
                {
                    if (length[j] == i)
                    {
                        perm[pp++] = j;
                    }
                }
            }

            for (int i = BZip2.MaxCodeLength; --i > 0;)
            {
                bbase[i] = 0;
                limit[i] = 0;
            }

            for (int i = 0; i < alphaSize; i++)
            {
                bbase[length[i] + 1]++;
            }

            for (int i = 1, b = bbase[0]; i < BZip2.MaxCodeLength; i++)
            {
                b += bbase[i];
                bbase[i] = b;
            }

            for (int i = minLen, vec = 0, b =  bbase[i]; i <= maxLen; i++)
            {
                int nb = bbase[i + 1];
                vec += nb - b;
                b = nb;
                limit[i] = vec - 1;
                vec <<= 1;
            }

            for (int i = minLen + 1; i <= maxLen; i++)
            {
                bbase[i] = ((limit[i - 1] + 1) << 1) - bbase[i];
            }
        }



        private void recvDecodingTables()
        {
            var s = this.data;
            bool[] inUse = s.inUse;
            byte[] pos = s.recvDecodingTables_pos;
            //byte[] selector = s.selector;

            int inUse16 = 0;

            /* Receive the mapping table */
            for (int i = 0; i < 16; i++)
            {
                if (bsGetBit())
                {
                    inUse16 |= 1 << i;
                }
            }

            for (int i = 256; --i >= 0;)
            {
                inUse[i] = false;
            }

            for (int i = 0; i < 16; i++)
            {
                if ((inUse16 & (1 << i)) != 0)
                {
                    int i16 = i << 4;
                    for (int j = 0; j < 16; j++)
                    {
                        if (bsGetBit())
                        {
                            inUse[i16 + j] = true;
                        }
                    }
                }
            }

            MakeMaps();
            int alphaSize = this.nInUse + 2;

            /* Now the selectors */
            int nGroups = GetBits(3);
            int nSelectors = GetBits(15);

            for (int i = 0; i < nSelectors; i++)
            {
                int j = 0;
                while (bsGetBit())
                {
                    j++;
                }
                s.selectorMtf[i] = (byte) j;
            }

            /* Undo the MTF values for the selectors. */
            for (int v = nGroups; --v >= 0;)
            {
                pos[v] = (byte) v;
            }

            for (int i = 0; i < nSelectors; i++)
            {
                int v = s.selectorMtf[i];
                byte tmp = pos[v];
                while (v > 0)
                {
                    // nearly all times v is zero, 4 in most other cases
                    pos[v] = pos[v - 1];
                    v--;
                }
                pos[0] = tmp;
                s.selector[i] = tmp;
            }

            char[][] len = s.temp_charArray2d;

            /* Now the coding tables */
            for (int t = 0; t < nGroups; t++)
            {
                int curr = GetBits(5);
                char[] len_t = len[t];
                for (int i = 0; i < alphaSize; i++)
                {
                    while (bsGetBit())
                    {
                        curr += bsGetBit() ? -1 : 1;
                    }
                    len_t[i] = (char) curr;
                }
            }

            // finally create the Huffman tables
            createHuffmanDecodingTables(alphaSize, nGroups);
        }


        /**
         * Called by recvDecodingTables() exclusively.
         */
        private void createHuffmanDecodingTables(int alphaSize,
                                                 int nGroups)
        {
            var s = this.data;
            char[][] len = s.temp_charArray2d;

            for (int t = 0; t < nGroups; t++)
            {
                int minLen = 32;
                int maxLen = 0;
                char[] len_t = len[t];
                for (int i = alphaSize; --i >= 0;)
                {
                    char lent = len_t[i];
                    if (lent > maxLen)
                        maxLen = lent;

                    if (lent < minLen)
                        minLen = lent;
                }
                hbCreateDecodeTables(s.gLimit[t], s.gBase[t], s.gPerm[t], len[t], minLen,
                                     maxLen, alphaSize);
                s.gMinlen[t] = minLen;
            }
        }



        private void getAndMoveToFrontDecode()
        {
            var s = this.data;
            this.origPtr = GetBits(24);

            if (this.origPtr < 0)
                throw new IOException("BZ_DATA_ERROR");
            if (this.origPtr > 10 + BZip2.BlockSizeMultiple * this.blockSize100k)
                throw new IOException("BZ_DATA_ERROR");

            recvDecodingTables();

            byte[] yy = s.getAndMoveToFrontDecode_yy;
            int limitLast = this.blockSize100k * BZip2.BlockSizeMultiple;

            /*
             * Setting up the unzftab entries here is not strictly necessary, but it
             * does save having to do it later in a separate pass, and so saves a
             * block's worth of cache misses.
             */
            for (int i = 256; --i >= 0;)
            {
                yy[i] = (byte) i;
                s.unzftab[i] = 0;
            }

            int groupNo = 0;
            int groupPos = BZip2.G_SIZE - 1;
            int eob = this.nInUse + 1;
            int nextSym = getAndMoveToFrontDecode0(0);
            int bsBuffShadow = this.bsBuff;
            int bsLiveShadow = this.bsLive;
            int lastShadow = -1;
            int zt = s.selector[groupNo] & 0xff;
            int[] base_zt = s.gBase[zt];
            int[] limit_zt = s.gLimit[zt];
            int[] perm_zt = s.gPerm[zt];
            int minLens_zt = s.gMinlen[zt];

            while (nextSym != eob)
            {
                if ((nextSym == BZip2.RUNA) || (nextSym == BZip2.RUNB))
                {
                    int es = -1;

                    for (int n = 1; true; n <<= 1)
                    {
                        if (nextSym == BZip2.RUNA)
                        {
                            es += n;
                        }
                        else if (nextSym == BZip2.RUNB)
                        {
                            es += n << 1;
                        }
                        else
                        {
                            break;
                        }

                        if (groupPos == 0)
                        {
                            groupPos = BZip2.G_SIZE - 1;
                            zt = s.selector[++groupNo] & 0xff;
                            base_zt = s.gBase[zt];
                            limit_zt = s.gLimit[zt];
                            perm_zt = s.gPerm[zt];
                            minLens_zt = s.gMinlen[zt];
                        }
                        else
                        {
                            groupPos--;
                        }

                        int zn = minLens_zt;

                        // Inlined:
                        // int zvec = GetBits(zn);
                        while (bsLiveShadow < zn)
                        {
                            int thech = this.input.ReadByte();
                            if (thech >= 0)
                            {
                                bsBuffShadow = (bsBuffShadow << 8) | thech;
                                bsLiveShadow += 8;
                                continue;
                            }
                            else
                            {
                                throw new IOException("unexpected end of stream");
                            }
                        }
                        int zvec = (bsBuffShadow >> (bsLiveShadow - zn))
                            & ((1 << zn) - 1);
                        bsLiveShadow -= zn;

                        while (zvec > limit_zt[zn])
                        {
                            zn++;
                            while (bsLiveShadow < 1)
                            {
                                int thech = this.input.ReadByte();
                                if (thech >= 0)
                                {
                                    bsBuffShadow = (bsBuffShadow << 8) | thech;
                                    bsLiveShadow += 8;
                                    continue;
                                }
                                else
                                {
                                    throw new IOException("unexpected end of stream");
                                }
                            }
                            bsLiveShadow--;
                            zvec = (zvec << 1)
                                | ((bsBuffShadow >> bsLiveShadow) & 1);
                        }
                        nextSym = perm_zt[zvec - base_zt[zn]];
                    }

                    byte ch = s.seqToUnseq[yy[0]];
                    s.unzftab[ch & 0xff] += es + 1;

                    while (es-- >= 0)
                    {
                        s.ll8[++lastShadow] = ch;
                    }

                    if (lastShadow >= limitLast)
                        throw new IOException("block overrun");
                }
                else
                {
                    if (++lastShadow >= limitLast)
                        throw new IOException("block overrun");

                    byte tmp = yy[nextSym - 1];
                    s.unzftab[s.seqToUnseq[tmp] & 0xff]++;
                    s.ll8[lastShadow] = s.seqToUnseq[tmp];

                    /*
                     * This loop is hammered during decompression, hence avoid
                     * native method call overhead of System.Buffer.BlockCopy for very
                     * small ranges to copy.
                     */
                    if (nextSym <= 16)
                    {
                        for (int j = nextSym - 1; j > 0;)
                        {
                            yy[j] = yy[--j];
                        }
                    }
                    else
                    {
                        System.Buffer.BlockCopy(yy, 0, yy, 1, nextSym - 1);
                    }

                    yy[0] = tmp;

                    if (groupPos == 0)
                    {
                        groupPos = BZip2.G_SIZE - 1;
                        zt = s.selector[++groupNo] & 0xff;
                        base_zt = s.gBase[zt];
                        limit_zt = s.gLimit[zt];
                        perm_zt = s.gPerm[zt];
                        minLens_zt = s.gMinlen[zt];
                    }
                    else
                    {
                        groupPos--;
                    }

                    int zn = minLens_zt;

                    // Inlined:
                    // int zvec = GetBits(zn);
                    while (bsLiveShadow < zn)
                    {
                        int thech = this.input.ReadByte();
                        if (thech >= 0)
                        {
                            bsBuffShadow = (bsBuffShadow << 8) | thech;
                            bsLiveShadow += 8;
                            continue;
                        }
                        else
                        {
                            throw new IOException("unexpected end of stream");
                        }
                    }
                    int zvec = (bsBuffShadow >> (bsLiveShadow - zn))
                        & ((1 << zn) - 1);
                    bsLiveShadow -= zn;

                    while (zvec > limit_zt[zn])
                    {
                        zn++;
                        while (bsLiveShadow < 1)
                        {
                            int thech = this.input.ReadByte();
                            if (thech >= 0)
                            {
                                bsBuffShadow = (bsBuffShadow << 8) | thech;
                                bsLiveShadow += 8;
                                continue;
                            }
                            else
                            {
                                throw new IOException("unexpected end of stream");
                            }
                        }
                        bsLiveShadow--;
                        zvec = (zvec << 1) | ((bsBuffShadow >> bsLiveShadow) & 1);
                    }
                    nextSym = perm_zt[zvec - base_zt[zn]];
                }
            }

            this.last = lastShadow;
            this.bsLive = bsLiveShadow;
            this.bsBuff = bsBuffShadow;
        }


        private int getAndMoveToFrontDecode0(int groupNo)
        {
            var s = this.data;
            int zt = s.selector[groupNo] & 0xff;
            int[] limit_zt = s.gLimit[zt];
            int zn = s.gMinlen[zt];
            int zvec = GetBits(zn);
            int bsLiveShadow = this.bsLive;
            int bsBuffShadow = this.bsBuff;

            while (zvec > limit_zt[zn])
            {
                zn++;
                while (bsLiveShadow < 1)
                {
                    int thech = this.input.ReadByte();

                    if (thech >= 0)
                    {
                        bsBuffShadow = (bsBuffShadow << 8) | thech;
                        bsLiveShadow += 8;
                        continue;
                    }
                    else
                    {
                        throw new IOException("unexpected end of stream");
                    }
                }
                bsLiveShadow--;
                zvec = (zvec << 1) | ((bsBuffShadow >> bsLiveShadow) & 1);
            }

            this.bsLive = bsLiveShadow;
            this.bsBuff = bsBuffShadow;

            return s.gPerm[zt][zvec - s.gBase[zt][zn]];
        }


        private void SetupBlock()
        {
            if (this.data == null)
                return;

            int i;
            var s = this.data;
            int[] tt = s.initTT(this.last + 1);

            //       xxxx

            /* Check: unzftab entries in range. */
            for (i = 0; i <= 255; i++)
            {
                if (s.unzftab[i] < 0 || s.unzftab[i] > this.last)
                    throw new Exception("BZ_DATA_ERROR");
            }

            /* Actually generate cftab. */
            s.cftab[0] = 0;
            for (i = 1; i <= 256; i++) s.cftab[i] = s.unzftab[i-1];
            for (i = 1; i <= 256; i++) s.cftab[i] += s.cftab[i-1];
            /* Check: cftab entries in range. */
            for (i = 0; i <= 256; i++)
            {
                if (s.cftab[i] < 0 || s.cftab[i] > this.last+1)
                {
                    var msg = String.Format("BZ_DATA_ERROR: cftab[{0}]={1} last={2}",
                                            i, s.cftab[i], this.last);
                    throw new Exception(msg);
                }
            }
            /* Check: cftab entries non-descending. */
            for (i = 1; i <= 256; i++)
            {
                if (s.cftab[i-1] > s.cftab[i])
                    throw new Exception("BZ_DATA_ERROR");
            }

            int lastShadow;
            for (i = 0, lastShadow = this.last; i <= lastShadow; i++)
            {
                tt[s.cftab[s.ll8[i] & 0xff]++] = i;
            }

            if ((this.origPtr < 0) || (this.origPtr >= tt.Length))
                throw new IOException("stream corrupted");

            this.su_tPos = tt[this.origPtr];
            this.su_count = 0;
            this.su_i2 = 0;
            this.su_ch2 = 256; /* not a valid 8-bit byte value?, and not EOF */

            if (this.blockRandomised)
            {
                this.su_rNToGo = 0;
                this.su_rTPos = 0;
                SetupRandPartA();
            }
            else
            {
                SetupNoRandPartA();
            }
        }



        private void SetupRandPartA()
        {
            if (this.su_i2 <= this.last)
            {
                this.su_chPrev = this.su_ch2;
                int su_ch2Shadow = this.data.ll8[this.su_tPos] & 0xff;
                this.su_tPos = this.data.tt[this.su_tPos];
                if (this.su_rNToGo == 0)
                {
                    this.su_rNToGo = Rand.Rnums(this.su_rTPos) - 1;
                    if (++this.su_rTPos == 512)
                    {
                        this.su_rTPos = 0;
                    }
                }
                else
                {
                    this.su_rNToGo--;
                }
                this.su_ch2 = su_ch2Shadow ^= (this.su_rNToGo == 1) ? 1 : 0;
                this.su_i2++;
                this.currentChar = su_ch2Shadow;
                this.currentState = CState.RAND_PART_B;
                this.crc.UpdateCRC((byte)su_ch2Shadow);
            }
            else
            {
                EndBlock();
                InitBlock();
                SetupBlock();
            }
        }

        private void SetupNoRandPartA()
        {
            if (this.su_i2 <= this.last)
            {
                this.su_chPrev = this.su_ch2;
                int su_ch2Shadow = this.data.ll8[this.su_tPos] & 0xff;
                this.su_ch2 = su_ch2Shadow;
                this.su_tPos = this.data.tt[this.su_tPos];
                this.su_i2++;
                this.currentChar = su_ch2Shadow;
                this.currentState = CState.NO_RAND_PART_B;
                this.crc.UpdateCRC((byte)su_ch2Shadow);
            }
            else
            {
                this.currentState = CState.NO_RAND_PART_A;
                EndBlock();
                InitBlock();
                SetupBlock();
            }
        }

        private void SetupRandPartB()
        {
            if (this.su_ch2 != this.su_chPrev)
            {
                this.currentState = CState.RAND_PART_A;
                this.su_count = 1;
                SetupRandPartA();
            }
            else if (++this.su_count >= 4)
            {
                this.su_z = (char) (this.data.ll8[this.su_tPos] & 0xff);
                this.su_tPos = this.data.tt[this.su_tPos];
                if (this.su_rNToGo == 0)
                {
                    this.su_rNToGo = Rand.Rnums(this.su_rTPos) - 1;
                    if (++this.su_rTPos == 512)
                    {
                        this.su_rTPos = 0;
                    }
                }
                else
                {
                    this.su_rNToGo--;
                }
                this.su_j2 = 0;
                this.currentState = CState.RAND_PART_C;
                if (this.su_rNToGo == 1)
                {
                    this.su_z ^= (char)1;
                }
                SetupRandPartC();
            }
            else
            {
                this.currentState = CState.RAND_PART_A;
                SetupRandPartA();
            }
        }

        private void SetupRandPartC()
        {
            if (this.su_j2 < this.su_z)
            {
                this.currentChar = this.su_ch2;
                this.crc.UpdateCRC((byte)this.su_ch2);
                this.su_j2++;
            }
            else
            {
                this.currentState = CState.RAND_PART_A;
                this.su_i2++;
                this.su_count = 0;
                SetupRandPartA();
            }
        }

        private void SetupNoRandPartB()
        {
            if (this.su_ch2 != this.su_chPrev)
            {
                this.su_count = 1;
                SetupNoRandPartA();
            }
            else if (++this.su_count >= 4)
            {
                this.su_z = (char) (this.data.ll8[this.su_tPos] & 0xff);
                this.su_tPos = this.data.tt[this.su_tPos];
                this.su_j2 = 0;
                SetupNoRandPartC();
            }
            else
            {
                SetupNoRandPartA();
            }
        }

        private void SetupNoRandPartC()
        {
            if (this.su_j2 < this.su_z)
            {
                int su_ch2Shadow = this.su_ch2;
                this.currentChar = su_ch2Shadow;
                this.crc.UpdateCRC((byte)su_ch2Shadow);
                this.su_j2++;
                this.currentState = CState.NO_RAND_PART_C;
            }
            else
            {
                this.su_i2++;
                this.su_count = 0;
                SetupNoRandPartA();
            }
        }

        private sealed class DecompressionState
        {
            // (with blockSize 900k)
            readonly public bool[] inUse = new bool[256];
            readonly public byte[] seqToUnseq = new byte[256]; // 256 byte
            readonly public byte[] selector = new byte[BZip2.MaxSelectors]; // 18002 byte
            readonly public byte[] selectorMtf = new byte[BZip2.MaxSelectors]; // 18002 byte

            /**
             * Freq table collected to save a pass over the data during
             * decompression.
             */
            public readonly int[] unzftab;
            public readonly int[][] gLimit;
            public readonly int[][] gBase;
            public readonly int[][] gPerm;
            public readonly int[] gMinlen;

            public readonly int[] cftab;
            public readonly byte[] getAndMoveToFrontDecode_yy;
            public readonly char[][] temp_charArray2d;
            public readonly byte[] recvDecodingTables_pos;
            // ---------------
            // 60798 byte

            public int[] tt; // 3600000 byte
            public byte[] ll8; // 900000 byte

            // ---------------
            // 4560782 byte
            // ===============

            public DecompressionState(int blockSize100k)
            {
                this.unzftab = new int[256]; // 1024 byte

                this.gLimit = BZip2.InitRectangularArray<int>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.gBase = BZip2.InitRectangularArray<int>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.gPerm = BZip2.InitRectangularArray<int>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.gMinlen = new int[BZip2.NGroups]; // 24 byte

                this.cftab = new int[257]; // 1028 byte
                this.getAndMoveToFrontDecode_yy = new byte[256]; // 512 byte
                this.temp_charArray2d = BZip2.InitRectangularArray<char>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.recvDecodingTables_pos = new byte[BZip2.NGroups]; // 6 byte

                this.ll8 = new byte[blockSize100k * BZip2.BlockSizeMultiple];
            }

            /**
             * Initializes the tt array.
             *
             * This method is called when the required length of the array is known.
             * I don't initialize it at construction time to avoid unneccessary
             * memory allocation when compressing small files.
             */
            public int[] initTT(int length)
            {
                int[] ttShadow = this.tt;

                // tt.length should always be >= length, but theoretically
                // it can happen, if the compressor mixed small and large
                // blocks. Normally only the last block will be smaller
                // than others.
                if ((ttShadow == null) || (ttShadow.Length < length))
                {
                    this.tt = ttShadow = new int[length];
                }

                return ttShadow;
            }
        }


    }

    // /**
    //  * Checks if the signature matches what is expected for a bzip2 file.
    //  *
    //  * @param signature
    //  *            the bytes to check
    //  * @param length
    //  *            the number of bytes to check
    //  * @return true, if this stream is a bzip2 compressed stream, false otherwise
    //  *
    //  * @since Apache Commons Compress 1.1
    //  */
    // public static boolean MatchesSig(byte[] signature)
    // {
    //     if ((signature.Length < 3) ||
    //         (signature[0] != 'B') ||
    //         (signature[1] != 'Z') ||
    //         (signature[2] != 'h'))
    //         return false;
    //
    //     return true;
    // }


    internal static class BZip2
    {
            internal static T[][] InitRectangularArray<T>(int d1, int d2)
            {
                var x = new T[d1][];
                for (int i=0; i < d1; i++)
                {
                    x[i] = new T[d2];
                }
                return x;
            }

        public static readonly int BlockSizeMultiple       = 100000;
        public static readonly int MinBlockSize       = 1;
        public static readonly int MaxBlockSize       = 9;
        public static readonly int MaxAlphaSize        = 258;
        public static readonly int MaxCodeLength       = 23;
        public static readonly char RUNA                = (char) 0;
        public static readonly char RUNB                = (char) 1;
        public static readonly int NGroups             = 6;
        public static readonly int G_SIZE              = 50;
        public static readonly int N_ITERS             = 4;
        public static readonly int MaxSelectors        = (2 + (900000 / G_SIZE));
        public static readonly int NUM_OVERSHOOT_BYTES = 20;
    /*
     * <p> If you are ever unlucky/improbable enough to get a stack
     * overflow whilst sorting, increase the following constant and
     * try again. In practice I have never seen the stack go above 27
     * elems, so the following limit seems very generous.  </p>
     */
        internal static readonly int QSORT_STACK_SIZE = 1000;


    }

}