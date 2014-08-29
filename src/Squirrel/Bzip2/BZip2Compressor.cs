// BZip2Compressor.cs
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
// Last Saved: <2011-July-28 06:17:22>
//
// ------------------------------------------------------------------
//
// This module defines the BZip2Compressor class, which is a
// BZIP2-compressing encoder.  It is used internally in the BZIP2
// library, by the BZip2OutputStream class and its parallel variant,
// ParallelBZip2OutputStream. This code was originally based on Apache
// commons source code, and significantly modified.  The license below
// applies to the original Apache code and to this modified variant.
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


//
// Design notes:
//
// This class performs BZip2 compression. It is derived from the
// BZip2OutputStream from the Apache commons source code, but is
// significantly modified from that code. While the Apache class is a
// stream that compresses, this particular class simply performs
// compression.  It follows a Manager pattern. It manages an internal
// buffer for uncompressed data; callers place data into the buffer
// using the Fill() method. This class then compresses the data and
// writes the compressed form out, via the CompressAndWrite() method.
// Because BZip2 uses byte-shredding, this class relies on a BitWriter,
// and not a .NET Stream, to emit its output.  (*Think of the BitWriter
// class as an Adapter that enables Bit-oriented output to a standard
// byte-oriented .NET stream.)
//
// This class exists to support the two distinct output streams that
// perform BZip2 compression: BZip2OutputStream and
// ParallelBZip2OutputStream. These streams rely on BZip2Compressor to
// provide the encoder/compression logic.  This code has been derived
// from the bzip2 output stream in the Apache commons library; it has
// been significantly modified from that form, in order to provide a
// single compressor that could support both types of streams.
//
// In a bz2 file or stream, there is never any bit padding except for 0..7
// bits in the final byte in the file. Successive compressed blocks in a
// .bz2 file are not byte-aligned.
//
//

using System;
using System.IO;

// flymake: csc.exe /t:module BZip2InputStream.cs BZip2OutputStream.cs Rand.cs BCRC32.cs @@FILE@@

namespace Ionic.BZip2
{
    internal class BZip2Compressor
    {
        private int blockSize100k;  // 0...9
        private int currentByte = -1;
        private int runLength = 0;
        private int last;  // index into the block of the last char processed
        private int outBlockFillThreshold;
        private CompressionState cstate;
        private readonly Ionic.Crc.CRC32 crc = new Ionic.Crc.CRC32(true);
        BitWriter bw;
        int runs;

        /*
         * The following three vars are used when sorting. If too many long
         * comparisons happen, we stop sorting, randomise the block slightly, and
         * try again. I think this wrinkle in the implementation was removed from
         * a later rev of the C-language bzip, not sure. -DPC 24 Jul 2011
         *
         */
        private int workDone;
        private int workLimit;
        private bool firstAttempt;
        private bool blockRandomised;
        private int origPtr;

        private int nInUse;
        private int nMTF;

        private static readonly int SETMASK = (1 << 21);
        private static readonly int CLEARMASK = (~SETMASK);
        private static readonly byte GREATER_ICOST = 15;
        private static readonly byte LESSER_ICOST = 0;
        private static readonly int SMALL_THRESH = 20;
        private static readonly int DEPTH_THRESH = 10;
        private static readonly int WORK_FACTOR = 30;

        /**
         * Knuth's increments seem to work better than Incerpi-Sedgewick here.
         * Possibly because the number of elems to sort is usually small, typically
         * &lt;= 20.
         */
        private static readonly int[] increments = { 1, 4, 13, 40, 121, 364, 1093, 3280,
                                                     9841, 29524, 88573, 265720, 797161,
                                                     2391484 };

        /// <summary>
        ///   BZip2Compressor writes its compressed data out via a BitWriter. This
        ///   is necessary because BZip2 does byte shredding.
        /// </summary>
        public BZip2Compressor(BitWriter writer)
            : this(writer, BZip2.MaxBlockSize)
        {
        }

        public BZip2Compressor(BitWriter writer, int blockSize)
        {
            this.blockSize100k = blockSize;
            this.bw = writer;

            // 20 provides a margin of slop (not to say "Safety"). The maximum
            // size of an encoded run in the output block is 5 bytes, so really, 5
            // bytes ought to do, but this is a margin of slop found in the
            // original bzip code. Not sure if important for decoding
            // (decompressing).  So we'll leave the slop.
            this.outBlockFillThreshold = (blockSize * BZip2.BlockSizeMultiple) - 20;
            this.cstate = new CompressionState(blockSize);
            Reset();
        }


        void Reset()
        {
            // initBlock();
            this.crc.Reset();
            this.currentByte = -1;
            this.runLength = 0;
            this.last = -1;
            for (int i = 256; --i >= 0;)
                this.cstate.inUse[i] = false;
            //bw.Reset();  xxx? want this?  no no no
        }


        public int BlockSize
        {
            get { return this.blockSize100k; }
        }

        public uint Crc32
        {
            get; private set;
        }

        public int AvailableBytesOut
        {
            get; private set;
        }

        /// <summary>
        ///   The number of uncompressed bytes being held in the buffer.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     I am thinking this may be useful in a Stream that uses this
        ///     compressor class. In the Close() method on the stream it could
        ///     check this value to see if anything has been written at all.  You
        ///     may think the stream could easily track the number of bytes it
        ///     wrote, which would eliminate the need for this. But, there is the
        ///     case where the stream writes a complete block, and it is full, and
        ///     then writes no more. In that case the stream may want to check.
        ///   </para>
        /// </remarks>
        public int UncompressedBytes
        {
            get { return this.last + 1; }
        }


        /// <summary>
        ///   Accept new bytes into the compressor data buffer
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This method does the first-level (cheap) run-length encoding, and
        ///     stores the encoded data into the rle block.
        ///   </para>
        /// </remarks>
        public int Fill(byte[] buffer, int offset, int count)
        {
            if (this.last >= this.outBlockFillThreshold)
                return 0; // We're full, I tell you!

            int bytesWritten = 0;
            int limit = offset + count;
            int rc;

            // do run-length-encoding until block is full
            do
            {
                rc = write0(buffer[offset++]);
                if (rc > 0) bytesWritten++;
            } while (offset < limit && rc == 1);

            return bytesWritten;
        }



        /// <summary>
        ///   Process one input byte into the block.
        /// </summary>
        ///
        /// <remarks>
        ///   <para>
        ///     To "process" the byte means to do the run-length encoding.
        ///     There are 3 possible return values:
        ///
        ///        0 - the byte was not written, in other words, not
        ///            encoded into the block. This happens when the
        ///            byte b would require the start of a new run, and
        ///            the block has no more room for new runs.
        ///
        ///        1 - the byte was written, and the block is not full.
        ///
        ///        2 - the byte was written, and the block is full.
        ///
        ///   </para>
        /// </remarks>
        /// <returns>0 if the byte was not written, non-zero if written.</returns>
        private int write0(byte b)
        {
            bool rc;
            // there is no current run in progress
            if (this.currentByte == -1)
            {
                this.currentByte = b;
                this.runLength++;
                return 1;
            }

            // this byte is the same as the current run in progress
            if (this.currentByte == b)
            {
                if (++this.runLength > 254)
                {
                    rc = AddRunToOutputBlock(false);
                    this.currentByte = -1;
                    this.runLength = 0;
                    return (rc) ? 2 : 1;
                }
                return 1; // not full
            }

            // This byte requires a new run.
            // Put the prior run into the Run-length-encoded block,
            // and try to start a new run.
            rc = AddRunToOutputBlock(false);

            if (rc)
            {
                this.currentByte = -1;
                this.runLength = 0;
                // returning 0 implies the block is full, and the byte was not written.
                return 0;
            }

            // start a new run
            this.runLength = 1;
            this.currentByte = b;
            return 1;
        }

        /// <summary>
        ///   Append one run to the output block.
        /// </summary>
        ///
        /// <remarks>
        ///   <para>
        ///     This compressor does run-length-encoding before BWT and etc. This
        ///     method simply appends a run to the output block. The append always
        ///     succeeds. The return value indicates whether the block is full:
        ///     false (not full) implies that at least one additional run could be
        ///     processed.
        ///   </para>
        /// </remarks>
        /// <returns>true if the block is now full; otherwise false.</returns>
        private bool AddRunToOutputBlock(bool final)
        {
            runs++;
            /* add_pair_to_block ( EState* s ) */
            int previousLast = this.last;

            // sanity check only - because of the check done at the
            // bottom of this method, and the logic in write0(), this
            // should never ever happen.
            if (previousLast >= this.outBlockFillThreshold && !final)
            {
                var msg = String.Format("block overrun(final={2}): {0} >= threshold ({1})",
                                        previousLast, this.outBlockFillThreshold, final);
                throw new Exception(msg);
            }

            // NB: the index used here into block is always (last+2).  This is
            // because last is -1 based - the initial value is -1, a flag value
            // used to indicate that nothing has yet been written into the
            // block. The endBlock() fn tests for -1 to detect empty blocks. Also,
            // the first byte of block is used, during sorting, to hold block[last
            // +1], which is the final byte value that had been written into the
            // rle block. For those two reasons, the base offset from last is
            // always +2.

            byte b = (byte) this.currentByte;
            byte[] block = this.cstate.block;
            this.cstate.inUse[b] = true;
            int rl = this.runLength;
            this.crc.UpdateCRC(b, rl);

            switch (rl)
            {
                case 1:
                    block[previousLast + 2] = b;
                    this.last = previousLast + 1;
                    break;

                case 2:
                    block[previousLast + 2] = b;
                    block[previousLast + 3] = b;
                    this.last = previousLast + 2;
                    break;

                case 3:
                    block[previousLast + 2] = b;
                    block[previousLast + 3] = b;
                    block[previousLast + 4] = b;
                    this.last = previousLast + 3;
                    break;

                default:
                    rl -= 4;
                    this.cstate.inUse[rl] = true;
                    block[previousLast + 2] = b;
                    block[previousLast + 3] = b;
                    block[previousLast + 4] = b;
                    block[previousLast + 5] = b;
                    block[previousLast + 6] = (byte) rl;
                    this.last = previousLast + 5;
                    break;
            }

            // is full?
            return (this.last >= this.outBlockFillThreshold);
        }


        /// <summary>
        ///   Compress the data that has been placed (Run-length-encoded) into the
        ///   block. The compressed data goes into the CompressedBytes array.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Side effects: 1.  fills the CompressedBytes array.  2. sets the
        ///     AvailableBytesOut property.
        ///   </para>
        /// </remarks>
        public void CompressAndWrite() // endBlock
        {
            if (this.runLength > 0)
                AddRunToOutputBlock(true);

            this.currentByte = -1;

            // Console.WriteLine("  BZip2Compressor:CompressAndWrite (r={0} bcrc={1:X8})",
            //                   runs, this.crc.Crc32Result);

            // has any data been written?
            if (this.last == -1)
                return; // no data; nothing to compress

            /* sort the block and establish posn of original string */
            blockSort();

            /*
             * A 6-byte block header, the value chosen arbitrarily as 0x314159265359
             * :-). A 32 bit value does not really give a strong enough guarantee
             * that the value will not appear by chance in the compressed
             * datastream. Worst-case probability of this event, for a 900k block,
             * is about 2.0e-3 for 32 bits, 1.0e-5 for 40 bits and 4.0e-8 for 48
             * bits. For a compressed file of size 100Gb -- about 100000 blocks --
             * only a 48-bit marker will do. NB: normal compression/ decompression
             * donot rely on these statistical properties. They are only important
             * when trying to recover blocks from damaged files.
             */
            this.bw.WriteByte(0x31);
            this.bw.WriteByte(0x41);
            this.bw.WriteByte(0x59);
            this.bw.WriteByte(0x26);
            this.bw.WriteByte(0x53);
            this.bw.WriteByte(0x59);

            this.Crc32 = (uint) this.crc.Crc32Result;
            this.bw.WriteInt(this.Crc32);

            /* Now a single bit indicating randomisation. */
            this.bw.WriteBits(1, (this.blockRandomised)?1U:0U);

            /* Finally, block's contents proper. */
            moveToFrontCodeAndSend();

            Reset();
        }


        private void randomiseBlock()
        {
            bool[] inUse = this.cstate.inUse;
            byte[] block = this.cstate.block;
            int lastShadow = this.last;

            for (int i = 256; --i >= 0;)
                inUse[i] = false;

            int rNToGo = 0;
            int rTPos = 0;
            for (int i = 0, j = 1; i <= lastShadow; i = j, j++)
            {
                if (rNToGo == 0)
                {
                    rNToGo = (char) Rand.Rnums(rTPos);
                    if (++rTPos == 512)
                    {
                        rTPos = 0;
                    }
                }

                rNToGo--;
                block[j] ^= (byte) ((rNToGo == 1) ? 1 : 0);

                // handle 16 bit signed numbers
                inUse[block[j] & 0xff] = true;
            }

            this.blockRandomised = true;
        }

        private void mainSort()
        {
            CompressionState dataShadow = this.cstate;
            int[] runningOrder = dataShadow.mainSort_runningOrder;
            int[] copy = dataShadow.mainSort_copy;
            bool[] bigDone = dataShadow.mainSort_bigDone;
            int[] ftab = dataShadow.ftab;
            byte[] block = dataShadow.block;
            int[] fmap = dataShadow.fmap;
            char[] quadrant = dataShadow.quadrant;
            int lastShadow = this.last;
            int workLimitShadow = this.workLimit;
            bool firstAttemptShadow = this.firstAttempt;

            // Set up the 2-byte frequency table
            for (int i = 65537; --i >= 0;)
            {
                ftab[i] = 0;
            }

            /*
             * In the various block-sized structures, live data runs from 0 to
             * last+NUM_OVERSHOOT_BYTES inclusive. First, set up the overshoot area
             * for block.
             */
            for (int i = 0; i < BZip2.NUM_OVERSHOOT_BYTES; i++)
            {
                block[lastShadow + i + 2] = block[(i % (lastShadow + 1)) + 1];
            }
            for (int i = lastShadow + BZip2.NUM_OVERSHOOT_BYTES +1; --i >= 0;)
            {
                quadrant[i] = '\0';
            }
            block[0] = block[lastShadow + 1];

            // Complete the initial radix sort:

            int c1 = block[0] & 0xff;
            for (int i = 0; i <= lastShadow; i++)
            {
                int c2 = block[i + 1] & 0xff;
                ftab[(c1 << 8) + c2]++;
                c1 = c2;
            }

            for (int i = 1; i <= 65536; i++)
                ftab[i] += ftab[i - 1];

            c1 = block[1] & 0xff;
            for (int i = 0; i < lastShadow; i++)
            {
                int c2 = block[i + 2] & 0xff;
                fmap[--ftab[(c1 << 8) + c2]] = i;
                c1 = c2;
            }

            fmap[--ftab[((block[lastShadow + 1] & 0xff) << 8) + (block[1] & 0xff)]] = lastShadow;

            /*
             * Now ftab contains the first loc of every small bucket. Calculate the
             * running order, from smallest to largest big bucket.
             */
            for (int i = 256; --i >= 0;)
            {
                bigDone[i] = false;
                runningOrder[i] = i;
            }

            for (int h = 364; h != 1;)
            {
                h /= 3;
                for (int i = h; i <= 255; i++)
                {
                    int vv = runningOrder[i];
                    int a = ftab[(vv + 1) << 8] - ftab[vv << 8];
                    int b = h - 1;
                    int j = i;
                    for (int ro = runningOrder[j - h]; (ftab[(ro + 1) << 8] - ftab[ro << 8]) > a; ro = runningOrder[j
                                                                                                                    - h])
                    {
                        runningOrder[j] = ro;
                        j -= h;
                        if (j <= b)
                        {
                            break;
                        }
                    }
                    runningOrder[j] = vv;
                }
            }

            /*
             * The main sorting loop.
             */
            for (int i = 0; i <= 255; i++)
            {
                /*
                 * Process big buckets, starting with the least full.
                 */
                int ss = runningOrder[i];

                // Step 1:
                /*
                 * Complete the big bucket [ss] by quicksorting any unsorted small
                 * buckets [ss, j]. Hopefully previous pointer-scanning phases have
                 * already completed many of the small buckets [ss, j], so we don't
                 * have to sort them at all.
                 */
                for (int j = 0; j <= 255; j++)
                {
                    int sb = (ss << 8) + j;
                    int ftab_sb = ftab[sb];
                    if ((ftab_sb & SETMASK) != SETMASK)
                    {
                        int lo = ftab_sb & CLEARMASK;
                        int hi = (ftab[sb + 1] & CLEARMASK) - 1;
                        if (hi > lo)
                        {
                            mainQSort3(dataShadow, lo, hi, 2);
                            if (firstAttemptShadow
                                && (this.workDone > workLimitShadow))
                            {
                                return;
                            }
                        }
                        ftab[sb] = ftab_sb | SETMASK;
                    }
                }

                // Step 2:
                // Now scan this big bucket so as to synthesise the
                // sorted order for small buckets [t, ss] for all t != ss.

                for (int j = 0; j <= 255; j++)
                {
                    copy[j] = ftab[(j << 8) + ss] & CLEARMASK;
                }

                for (int j = ftab[ss << 8] & CLEARMASK, hj = (ftab[(ss + 1) << 8] & CLEARMASK); j < hj; j++)
                {
                    int fmap_j = fmap[j];
                    c1 = block[fmap_j] & 0xff;
                    if (!bigDone[c1])
                    {
                        fmap[copy[c1]] = (fmap_j == 0) ? lastShadow : (fmap_j - 1);
                        copy[c1]++;
                    }
                }

                for (int j = 256; --j >= 0;)
                    ftab[(j << 8) + ss] |= SETMASK;

                // Step 3:
                /*
                 * The ss big bucket is now done. Record this fact, and update the
                 * quadrant descriptors. Remember to update quadrants in the
                 * overshoot area too, if necessary. The "if (i < 255)" test merely
                 * skips this updating for the last bucket processed, since updating
                 * for the last bucket is pointless.
                 */
                bigDone[ss] = true;

                if (i < 255)
                {
                    int bbStart = ftab[ss << 8] & CLEARMASK;
                    int bbSize = (ftab[(ss + 1) << 8] & CLEARMASK) - bbStart;
                    int shifts = 0;

                    while ((bbSize >> shifts) > 65534)
                    {
                        shifts++;
                    }

                    for (int j = 0; j < bbSize; j++)
                    {
                        int a2update = fmap[bbStart + j];
                        char qVal = (char) (j >> shifts);
                        quadrant[a2update] = qVal;
                        if (a2update < BZip2.NUM_OVERSHOOT_BYTES)
                        {
                            quadrant[a2update + lastShadow + 1] = qVal;
                        }
                    }
                }

            }
        }


        private void blockSort()
        {
            this.workLimit = WORK_FACTOR * this.last;
            this.workDone = 0;
            this.blockRandomised = false;
            this.firstAttempt = true;
            mainSort();

            if (this.firstAttempt && (this.workDone > this.workLimit))
            {
                randomiseBlock();
                this.workLimit = this.workDone = 0;
                this.firstAttempt = false;
                mainSort();
            }

            int[] fmap = this.cstate.fmap;
            this.origPtr = -1;
            for (int i = 0, lastShadow = this.last; i <= lastShadow; i++)
            {
                if (fmap[i] == 0)
                {
                    this.origPtr = i;
                    break;
                }
            }

            // assert (this.origPtr != -1) : this.origPtr;
        }


        /**
         * This is the most hammered method of this class.
         *
         * <p>
         * This is the version using unrolled loops.
         * </p>
         */
        private bool mainSimpleSort(CompressionState dataShadow, int lo,
                                    int hi, int d)
        {
            int bigN = hi - lo + 1;
            if (bigN < 2)
            {
                return this.firstAttempt && (this.workDone > this.workLimit);
            }

            int hp = 0;
            while (increments[hp] < bigN)
                hp++;

            int[] fmap = dataShadow.fmap;
            char[] quadrant = dataShadow.quadrant;
            byte[] block = dataShadow.block;
            int lastShadow = this.last;
            int lastPlus1 = lastShadow + 1;
            bool firstAttemptShadow = this.firstAttempt;
            int workLimitShadow = this.workLimit;
            int workDoneShadow = this.workDone;

            // Following block contains unrolled code which could be shortened by
            // coding it in additional loops.

            // HP:
            while (--hp >= 0)
            {
                int h = increments[hp];
                int mj = lo + h - 1;

                for (int i = lo + h; i <= hi;)
                {
                    // copy
                    for (int k = 3; (i <= hi) && (--k >= 0); i++)
                    {
                        int v = fmap[i];
                        int vd = v + d;
                        int j = i;

                        // for (int a;
                        // (j > mj) && mainGtU((a = fmap[j - h]) + d, vd,
                        // block, quadrant, lastShadow);
                        // j -= h) {
                        // fmap[j] = a;
                        // }
                        //
                        // unrolled version:

                        // start inline mainGTU
                        bool onceRunned = false;
                        int a = 0;

                        HAMMER: while (true)
                        {
                            if (onceRunned)
                            {
                                fmap[j] = a;
                                if ((j -= h) <= mj)
                                {
                                    goto END_HAMMER;
                                }
                            }
                            else {
                                onceRunned = true;
                            }

                            a = fmap[j - h];
                            int i1 = a + d;
                            int i2 = vd;

                            // following could be done in a loop, but
                            // unrolled it for performance:
                            if (block[i1 + 1] == block[i2 + 1])
                            {
                                if (block[i1 + 2] == block[i2 + 2])
                                {
                                    if (block[i1 + 3] == block[i2 + 3])
                                    {
                                        if (block[i1 + 4] == block[i2 + 4])
                                        {
                                            if (block[i1 + 5] == block[i2 + 5])
                                            {
                                                if (block[(i1 += 6)] == block[(i2 += 6)])
                                                {
                                                    int x = lastShadow;
                                                    X: while (x > 0)
                                                    {
                                                        x -= 4;

                                                        if (block[i1 + 1] == block[i2 + 1])
                                                        {
                                                            if (quadrant[i1] == quadrant[i2])
                                                            {
                                                                if (block[i1 + 2] == block[i2 + 2])
                                                                {
                                                                    if (quadrant[i1 + 1] == quadrant[i2 + 1])
                                                                    {
                                                                        if (block[i1 + 3] == block[i2 + 3])
                                                                        {
                                                                            if (quadrant[i1 + 2] == quadrant[i2 + 2])
                                                                            {
                                                                                if (block[i1 + 4] == block[i2 + 4])
                                                                                {
                                                                                    if (quadrant[i1 + 3] == quadrant[i2 + 3])
                                                                                    {
                                                                                        if ((i1 += 4) >= lastPlus1)
                                                                                        {
                                                                                            i1 -= lastPlus1;
                                                                                        }
                                                                                        if ((i2 += 4) >= lastPlus1)
                                                                                        {
                                                                                            i2 -= lastPlus1;
                                                                                        }
                                                                                        workDoneShadow++;
                                                                                        goto X;
                                                                                    }
                                                                                    else if ((quadrant[i1 + 3] > quadrant[i2 + 3]))
                                                                                    {
                                                                                        goto HAMMER;
                                                                                    }
                                                                                    else {
                                                                                        goto END_HAMMER;
                                                                                    }
                                                                                }
                                                                                else if ((block[i1 + 4] & 0xff) > (block[i2 + 4] & 0xff))
                                                                                {
                                                                                    goto HAMMER;
                                                                                }
                                                                                else {
                                                                                    goto END_HAMMER;
                                                                                }
                                                                            }
                                                                            else if ((quadrant[i1 + 2] > quadrant[i2 + 2]))
                                                                            {
                                                                                goto HAMMER;
                                                                            }
                                                                            else {
                                                                                goto END_HAMMER;
                                                                            }
                                                                        }
                                                                        else if ((block[i1 + 3] & 0xff) > (block[i2 + 3] & 0xff))
                                                                        {
                                                                            goto HAMMER;
                                                                        }
                                                                        else {
                                                                            goto END_HAMMER;
                                                                        }
                                                                    }
                                                                    else if ((quadrant[i1 + 1] > quadrant[i2 + 1]))
                                                                    {
                                                                        goto HAMMER;
                                                                    }
                                                                    else {
                                                                        goto END_HAMMER;
                                                                    }
                                                                }
                                                                else if ((block[i1 + 2] & 0xff) > (block[i2 + 2] & 0xff))
                                                                {
                                                                    goto HAMMER;
                                                                }
                                                                else {
                                                                    goto END_HAMMER;
                                                                }
                                                            }
                                                            else if ((quadrant[i1] > quadrant[i2]))
                                                            {
                                                                goto HAMMER;
                                                            }
                                                            else {
                                                                goto END_HAMMER;
                                                            }
                                                        }
                                                        else if ((block[i1 + 1] & 0xff) > (block[i2 + 1] & 0xff))
                                                        {
                                                            goto HAMMER;
                                                        }
                                                        else {
                                                            goto END_HAMMER;
                                                        }

                                                    }
                                                    goto END_HAMMER;
                                                } // while x > 0
                                                else {
                                                    if ((block[i1] & 0xff) > (block[i2] & 0xff))
                                                    {
                                                        goto HAMMER;
                                                    }
                                                    else {
                                                        goto END_HAMMER;
                                                    }
                                                }
                                            }
                                            else if ((block[i1 + 5] & 0xff) > (block[i2 + 5] & 0xff))
                                            {
                                                goto HAMMER;
                                            }
                                            else {
                                                goto END_HAMMER;
                                            }
                                        }
                                        else if ((block[i1 + 4] & 0xff) > (block[i2 + 4] & 0xff))
                                        {
                                            goto HAMMER;
                                        }
                                        else {
                                            goto END_HAMMER;
                                        }
                                    }
                                    else if ((block[i1 + 3] & 0xff) > (block[i2 + 3] & 0xff))
                                    {
                                        goto HAMMER;
                                    }
                                    else {
                                        goto END_HAMMER;
                                    }
                                }
                                else if ((block[i1 + 2] & 0xff) > (block[i2 + 2] & 0xff))
                                {
                                    goto HAMMER;
                                }
                                else {
                                    goto END_HAMMER;
                                }
                            }
                            else if ((block[i1 + 1] & 0xff) > (block[i2 + 1] & 0xff))
                            {
                                goto HAMMER;
                            }
                            else {
                                goto END_HAMMER;
                            }

                        } // HAMMER

                        END_HAMMER:
                        // end inline mainGTU

                        fmap[j] = v;
                    }

                    if (firstAttemptShadow && (i <= hi)
                        && (workDoneShadow > workLimitShadow))
                    {
                        goto END_HP;
                    }
                }
            }
            END_HP:

            this.workDone = workDoneShadow;
            return firstAttemptShadow && (workDoneShadow > workLimitShadow);
        }



        private static void vswap(int[] fmap, int p1, int p2, int n)
        {
            n += p1;
            while (p1 < n)
            {
                int t = fmap[p1];
                fmap[p1++] = fmap[p2];
                fmap[p2++] = t;
            }
        }

        private static byte med3(byte a, byte b, byte c)
        {
            return (a < b) ? (b < c ? b : a < c ? c : a) : (b > c ? b : a > c ? c
                                                            : a);
        }


        /**
         * Method "mainQSort3", file "blocksort.c", BZip2 1.0.2
         */
        private void mainQSort3(CompressionState dataShadow, int loSt,
                                int hiSt, int dSt)
        {
            int[] stack_ll = dataShadow.stack_ll;
            int[] stack_hh = dataShadow.stack_hh;
            int[] stack_dd = dataShadow.stack_dd;
            int[] fmap = dataShadow.fmap;
            byte[] block = dataShadow.block;

            stack_ll[0] = loSt;
            stack_hh[0] = hiSt;
            stack_dd[0] = dSt;

            for (int sp = 1; --sp >= 0;)
            {
                int lo = stack_ll[sp];
                int hi = stack_hh[sp];
                int d = stack_dd[sp];

                if ((hi - lo < SMALL_THRESH) || (d > DEPTH_THRESH))
                {
                    if (mainSimpleSort(dataShadow, lo, hi, d))
                    {
                        return;
                    }
                }
                else {
                    int d1 = d + 1;
                    int med = med3(block[fmap[lo] + d1],
                                   block[fmap[hi] + d1], block[fmap[(lo + hi) >> 1] + d1]) & 0xff;

                    int unLo = lo;
                    int unHi = hi;
                    int ltLo = lo;
                    int gtHi = hi;

                    while (true)
                    {
                        while (unLo <= unHi)
                        {
                            int n = (block[fmap[unLo] + d1] & 0xff)
                                - med;
                            if (n == 0)
                            {
                                int temp = fmap[unLo];
                                fmap[unLo++] = fmap[ltLo];
                                fmap[ltLo++] = temp;
                            }
                            else if (n < 0)
                            {
                                unLo++;
                            }
                            else {
                                break;
                            }
                        }

                        while (unLo <= unHi)
                        {
                            int n = (block[fmap[unHi] + d1] & 0xff)
                                - med;
                            if (n == 0)
                            {
                                int temp = fmap[unHi];
                                fmap[unHi--] = fmap[gtHi];
                                fmap[gtHi--] = temp;
                            }
                            else if (n > 0)
                            {
                                unHi--;
                            }
                            else {
                                break;
                            }
                        }

                        if (unLo <= unHi)
                        {
                            int temp = fmap[unLo];
                            fmap[unLo++] = fmap[unHi];
                            fmap[unHi--] = temp;
                        }
                        else {
                            break;
                        }
                    }

                    if (gtHi < ltLo)
                    {
                        stack_ll[sp] = lo;
                        stack_hh[sp] = hi;
                        stack_dd[sp] = d1;
                        sp++;
                    }
                    else {
                        int n = ((ltLo - lo) < (unLo - ltLo)) ? (ltLo - lo)
                            : (unLo - ltLo);
                        vswap(fmap, lo, unLo - n, n);
                        int m = ((hi - gtHi) < (gtHi - unHi)) ? (hi - gtHi)
                            : (gtHi - unHi);
                        vswap(fmap, unLo, hi - m + 1, m);

                        n = lo + unLo - ltLo - 1;
                        m = hi - (gtHi - unHi) + 1;

                        stack_ll[sp] = lo;
                        stack_hh[sp] = n;
                        stack_dd[sp] = d;
                        sp++;

                        stack_ll[sp] = n + 1;
                        stack_hh[sp] = m - 1;
                        stack_dd[sp] = d1;
                        sp++;

                        stack_ll[sp] = m;
                        stack_hh[sp] = hi;
                        stack_dd[sp] = d;
                        sp++;
                    }
                }
            }
        }



        private void generateMTFValues()
        {
            int lastShadow = this.last;
            CompressionState dataShadow = this.cstate;
            bool[] inUse = dataShadow.inUse;
            byte[] block = dataShadow.block;
            int[] fmap = dataShadow.fmap;
            char[] sfmap = dataShadow.sfmap;
            int[] mtfFreq = dataShadow.mtfFreq;
            byte[] unseqToSeq = dataShadow.unseqToSeq;
            byte[] yy = dataShadow.generateMTFValues_yy;

            // make maps
            int nInUseShadow = 0;
            for (int i = 0; i < 256; i++)
            {
                if (inUse[i])
                {
                    unseqToSeq[i] = (byte) nInUseShadow;
                    nInUseShadow++;
                }
            }
            this.nInUse = nInUseShadow;

            int eob = nInUseShadow + 1;

            for (int i = eob; i >= 0; i--)
            {
                mtfFreq[i] = 0;
            }

            for (int i = nInUseShadow; --i >= 0;)
            {
                yy[i] = (byte) i;
            }

            int wr = 0;
            int zPend = 0;

            for (int i = 0; i <= lastShadow; i++)
            {
                byte ll_i = unseqToSeq[block[fmap[i]] & 0xff];
                byte tmp = yy[0];
                int j = 0;

                while (ll_i != tmp)
                {
                    j++;
                    byte tmp2 = tmp;
                    tmp = yy[j];
                    yy[j] = tmp2;
                }
                yy[0] = tmp;

                if (j == 0)
                {
                    zPend++;
                }
                else
                {
                    if (zPend > 0)
                    {
                        zPend--;
                        while (true)
                        {
                            if ((zPend & 1) == 0)
                            {
                                sfmap[wr] = BZip2.RUNA;
                                wr++;
                                mtfFreq[BZip2.RUNA]++;
                            }
                            else
                            {
                                sfmap[wr] = BZip2.RUNB;
                                wr++;
                                mtfFreq[BZip2.RUNB]++;
                            }

                            if (zPend >= 2)
                            {
                                zPend = (zPend - 2) >> 1;
                            }
                            else
                            {
                                break;
                            }
                        }
                        zPend = 0;
                    }
                    sfmap[wr] = (char) (j + 1);
                    wr++;
                    mtfFreq[j + 1]++;
                }
            }

            if (zPend > 0)
            {
                zPend--;
                while (true)
                {
                    if ((zPend & 1) == 0)
                    {
                        sfmap[wr] = BZip2.RUNA;
                        wr++;
                        mtfFreq[BZip2.RUNA]++;
                    }
                    else
                    {
                        sfmap[wr] = BZip2.RUNB;
                        wr++;
                        mtfFreq[BZip2.RUNB]++;
                    }

                    if (zPend >= 2)
                    {
                        zPend = (zPend - 2) >> 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            sfmap[wr] = (char) eob;
            mtfFreq[eob]++;
            this.nMTF = wr + 1;
        }


        private static void hbAssignCodes(int[] code,  byte[] length,
                                          int minLen, int maxLen,
                                          int alphaSize)
        {
            int vec = 0;
            for (int n = minLen; n <= maxLen; n++)
            {
                for (int i = 0; i < alphaSize; i++)
                {
                    if ((length[i] & 0xff) == n)
                    {
                        code[i] = vec;
                        vec++;
                    }
                }
                vec <<= 1;
            }
        }




        private void sendMTFValues()
        {
            byte[][] len = this.cstate.sendMTFValues_len;
            int alphaSize = this.nInUse + 2;

            for (int t = BZip2.NGroups; --t >= 0;)
            {
                byte[] len_t = len[t];
                for (int v = alphaSize; --v >= 0;)
                {
                    len_t[v] = GREATER_ICOST;
                }
            }

            /* Decide how many coding tables to use */
            // assert (this.nMTF > 0) : this.nMTF;
            int nGroups = (this.nMTF < 200) ? 2 : (this.nMTF < 600) ? 3
                : (this.nMTF < 1200) ? 4 : (this.nMTF < 2400) ? 5 : 6;

            /* Generate an initial set of coding tables */
            sendMTFValues0(nGroups, alphaSize);

            /*
             * Iterate up to N_ITERS times to improve the tables.
             */
            int nSelectors = sendMTFValues1(nGroups, alphaSize);

            /* Compute MTF values for the selectors. */
            sendMTFValues2(nGroups, nSelectors);

            /* Assign actual codes for the tables. */
            sendMTFValues3(nGroups, alphaSize);

            /* Transmit the mapping table. */
            sendMTFValues4();

            /* Now the selectors. */
            sendMTFValues5(nGroups, nSelectors);

            /* Now the coding tables. */
            sendMTFValues6(nGroups, alphaSize);

            /* And finally, the block data proper */
            sendMTFValues7(nSelectors);
        }

        private void sendMTFValues0(int nGroups, int alphaSize)
        {
            byte[][] len = this.cstate.sendMTFValues_len;
            int[] mtfFreq = this.cstate.mtfFreq;

            int remF = this.nMTF;
            int gs = 0;

            for (int nPart = nGroups; nPart > 0; nPart--)
            {
                int tFreq = remF / nPart;
                int ge = gs - 1;
                int aFreq = 0;

                for (int a = alphaSize - 1; (aFreq < tFreq) && (ge < a);)
                {
                    aFreq += mtfFreq[++ge];
                }

                if ((ge > gs) && (nPart != nGroups) && (nPart != 1)
                    && (((nGroups - nPart) & 1) != 0))
                {
                    aFreq -= mtfFreq[ge--];
                }

                byte[] len_np = len[nPart - 1];
                for (int v = alphaSize; --v >= 0;)
                {
                    if ((v >= gs) && (v <= ge))
                    {
                        len_np[v] = LESSER_ICOST;
                    }
                    else {
                        len_np[v] = GREATER_ICOST;
                    }
                }

                gs = ge + 1;
                remF -= aFreq;
            }
        }


        private static void hbMakeCodeLengths(byte[] len,  int[] freq,
                                              CompressionState state1, int alphaSize,
                                              int maxLen)
        {
            /*
             * Nodes and heap entries run from 1. Entry 0 for both the heap and
             * nodes is a sentinel.
             */
            int[] heap = state1.heap;
            int[] weight = state1.weight;
            int[] parent = state1.parent;

            for (int i = alphaSize; --i >= 0;)
            {
                weight[i + 1] = (freq[i] == 0 ? 1 : freq[i]) << 8;
            }

            for (bool tooLong = true; tooLong;)
            {
                tooLong = false;

                int nNodes = alphaSize;
                int nHeap = 0;
                heap[0] = 0;
                weight[0] = 0;
                parent[0] = -2;

                for (int i = 1; i <= alphaSize; i++)
                {
                    parent[i] = -1;
                    nHeap++;
                    heap[nHeap] = i;

                    int zz = nHeap;
                    int tmp = heap[zz];
                    while (weight[tmp] < weight[heap[zz >> 1]])
                    {
                        heap[zz] = heap[zz >> 1];
                        zz >>= 1;
                    }
                    heap[zz] = tmp;
                }

                while (nHeap > 1)
                {
                    int n1 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;

                    int yy = 0;
                    int zz = 1;
                    int tmp = heap[1];

                    while (true)
                    {
                        yy = zz << 1;

                        if (yy > nHeap)
                        {
                            break;
                        }

                        if ((yy < nHeap)
                            && (weight[heap[yy + 1]] < weight[heap[yy]]))
                        {
                            yy++;
                        }

                        if (weight[tmp] < weight[heap[yy]])
                        {
                            break;
                        }

                        heap[zz] = heap[yy];
                        zz = yy;
                    }

                    heap[zz] = tmp;

                    int n2 = heap[1];
                    heap[1] = heap[nHeap];
                    nHeap--;

                    yy = 0;
                    zz = 1;
                    tmp = heap[1];

                    while (true)
                    {
                        yy = zz << 1;

                        if (yy > nHeap)
                        {
                            break;
                        }

                        if ((yy < nHeap)
                            && (weight[heap[yy + 1]] < weight[heap[yy]]))
                        {
                            yy++;
                        }

                        if (weight[tmp] < weight[heap[yy]])
                        {
                            break;
                        }

                        heap[zz] = heap[yy];
                        zz = yy;
                    }

                    heap[zz] = tmp;
                    nNodes++;
                    parent[n1] = parent[n2] = nNodes;

                    int weight_n1 = weight[n1];
                    int weight_n2 = weight[n2];
                    weight[nNodes] = (int) (((uint)weight_n1 & 0xffffff00U)
                                            + ((uint)weight_n2 & 0xffffff00U))
                        | (1 + (((weight_n1 & 0x000000ff)
                                 > (weight_n2 & 0x000000ff))
                                ? (weight_n1 & 0x000000ff)
                                : (weight_n2 & 0x000000ff)));

                    parent[nNodes] = -1;
                    nHeap++;
                    heap[nHeap] = nNodes;

                    tmp = 0;
                    zz = nHeap;
                    tmp = heap[zz];
                    int weight_tmp = weight[tmp];
                    while (weight_tmp < weight[heap[zz >> 1]])
                    {
                        heap[zz] = heap[zz >> 1];
                        zz >>= 1;
                    }
                    heap[zz] = tmp;

                }

                for (int i = 1; i <= alphaSize; i++)
                {
                    int j = 0;
                    int k = i;

                    for (int parent_k; (parent_k = parent[k]) >= 0;)
                    {
                        k = parent_k;
                        j++;
                    }

                    len[i - 1] = (byte) j;
                    if (j > maxLen)
                    {
                        tooLong = true;
                    }
                }

                if (tooLong)
                {
                    for (int i = 1; i < alphaSize; i++)
                    {
                        int j = weight[i] >> 8;
                        j = 1 + (j >> 1);
                        weight[i] = j << 8;
                    }
                }
            }
        }


        private int sendMTFValues1(int nGroups, int alphaSize)
        {
            CompressionState dataShadow = this.cstate;
            int[][] rfreq = dataShadow.sendMTFValues_rfreq;
            int[] fave = dataShadow.sendMTFValues_fave;
            short[] cost = dataShadow.sendMTFValues_cost;
            char[] sfmap = dataShadow.sfmap;
            byte[] selector = dataShadow.selector;
            byte[][] len = dataShadow.sendMTFValues_len;
            byte[] len_0 = len[0];
            byte[] len_1 = len[1];
            byte[] len_2 = len[2];
            byte[] len_3 = len[3];
            byte[] len_4 = len[4];
            byte[] len_5 = len[5];
            int nMTFShadow = this.nMTF;

            int nSelectors = 0;

            for (int iter = 0; iter < BZip2.N_ITERS; iter++)
            {
                for (int t = nGroups; --t >= 0;)
                {
                    fave[t] = 0;
                    int[] rfreqt = rfreq[t];
                    for (int i = alphaSize; --i >= 0;)
                    {
                        rfreqt[i] = 0;
                    }
                }

                nSelectors = 0;

                for (int gs = 0; gs < this.nMTF;)
                {
                    /* Set group start & end marks. */

                    /*
                     * Calculate the cost of this group as coded by each of the
                     * coding tables.
                     */

                    int ge = Math.Min(gs + BZip2.G_SIZE - 1, nMTFShadow - 1);

                    if (nGroups == BZip2.NGroups)
                    {
                        // unrolled version of the else-block

                        int[] c = new int[6];

                        for (int i = gs; i <= ge; i++)
                        {
                            int icv = sfmap[i];
                            c[0] += len_0[icv] & 0xff;
                            c[1] += len_1[icv] & 0xff;
                            c[2] += len_2[icv] & 0xff;
                            c[3] += len_3[icv] & 0xff;
                            c[4] += len_4[icv] & 0xff;
                            c[5] += len_5[icv] & 0xff;
                        }

                        cost[0] = (short) c[0];
                        cost[1] = (short) c[1];
                        cost[2] = (short) c[2];
                        cost[3] = (short) c[3];
                        cost[4] = (short) c[4];
                        cost[5] = (short) c[5];
                    }
                    else
                    {
                        for (int t = nGroups; --t >= 0;)
                        {
                            cost[t] = 0;
                        }

                        for (int i = gs; i <= ge; i++)
                        {
                            int icv = sfmap[i];
                            for (int t = nGroups; --t >= 0;)
                            {
                                cost[t] += (short) (len[t][icv] & 0xff);
                            }
                        }
                    }

                    /*
                     * Find the coding table which is best for this group, and
                     * record its identity in the selector table.
                     */
                    int bt = -1;
                    for (int t = nGroups, bc = 999999999; --t >= 0;)
                    {
                        int cost_t = cost[t];
                        if (cost_t < bc)
                        {
                            bc = cost_t;
                            bt = t;
                        }
                    }

                    fave[bt]++;
                    selector[nSelectors] = (byte) bt;
                    nSelectors++;

                    /*
                     * Increment the symbol frequencies for the selected table.
                     */
                    int[] rfreq_bt = rfreq[bt];
                    for (int i = gs; i <= ge; i++)
                    {
                        rfreq_bt[sfmap[i]]++;
                    }

                    gs = ge + 1;
                }

                /*
                 * Recompute the tables based on the accumulated frequencies.
                 */
                for (int t = 0; t < nGroups; t++)
                {
                    hbMakeCodeLengths(len[t], rfreq[t], this.cstate, alphaSize, 20);
                }
            }

            return nSelectors;
        }

        private void sendMTFValues2(int nGroups, int nSelectors)
        {
            // assert (nGroups < 8) : nGroups;

            CompressionState dataShadow = this.cstate;
            byte[] pos = dataShadow.sendMTFValues2_pos;

            for (int i = nGroups; --i >= 0;)
            {
                pos[i] = (byte) i;
            }

            for (int i = 0; i < nSelectors; i++)
            {
                byte ll_i = dataShadow.selector[i];
                byte tmp = pos[0];
                int j = 0;

                while (ll_i != tmp)
                {
                    j++;
                    byte tmp2 = tmp;
                    tmp = pos[j];
                    pos[j] = tmp2;
                }

                pos[0] = tmp;
                dataShadow.selectorMtf[i] = (byte) j;
            }
        }

        private void sendMTFValues3(int nGroups, int alphaSize)
        {
            int[][] code = this.cstate.sendMTFValues_code;
            byte[][] len = this.cstate.sendMTFValues_len;

            for (int t = 0; t < nGroups; t++)
            {
                int minLen = 32;
                int maxLen = 0;
                byte[] len_t = len[t];
                for (int i = alphaSize; --i >= 0;)
                {
                    int l = len_t[i] & 0xff;
                    if (l > maxLen)
                    {
                        maxLen = l;
                    }
                    if (l < minLen)
                    {
                        minLen = l;
                    }
                }

                // assert (maxLen <= 20) : maxLen;
                // assert (minLen >= 1) : minLen;

                hbAssignCodes(code[t], len[t], minLen, maxLen, alphaSize);
            }
        }

        private void sendMTFValues4()
        {
            bool[] inUse = this.cstate.inUse;
            bool[] inUse16 = this.cstate.sentMTFValues4_inUse16;

            for (int i = 16; --i >= 0;)
            {
                inUse16[i] = false;
                int i16 = i * 16;
                for (int j = 16; --j >= 0;)
                {
                    if (inUse[i16 + j])
                    {
                        inUse16[i] = true;
                    }
                }
            }

            uint u = 0;
            for (int i = 0; i < 16; i++)
            {
                if (inUse16[i])
                    u |= 1U << (16 - i - 1);
            }
            this.bw.WriteBits(16, u);


            for (int i = 0; i < 16; i++)
            {
                if (inUse16[i])
                {
                    int i16 = i * 16;
                    u = 0;
                    for (int j = 0; j < 16; j++)
                    {
                        if (inUse[i16 + j])
                        {
                            u |= 1U << (16 - j - 1);
                        }
                    }
                    this.bw.WriteBits(16, u);
                }
            }
        }


        private void sendMTFValues5(int nGroups, int nSelectors)
        {
            this.bw.WriteBits(3, (uint) nGroups);
            this.bw.WriteBits(15, (uint) nSelectors);

            byte[] selectorMtf = this.cstate.selectorMtf;

            for (int i = 0; i < nSelectors; i++)
            {
                for (int j = 0, hj = selectorMtf[i] & 0xff; j < hj; j++)
                {
                    this.bw.WriteBits(1, 1);
                }

                this.bw.WriteBits(1, 0);
            }
        }

        private void sendMTFValues6(int nGroups, int alphaSize)
        {
            byte[][] len = this.cstate.sendMTFValues_len;

            for (int t = 0; t < nGroups; t++)
            {
                byte[] len_t = len[t];
                uint curr = (uint) (len_t[0] & 0xff);
                this.bw.WriteBits(5, curr);

                for (int i = 0; i < alphaSize; i++)
                {
                    int lti = len_t[i] & 0xff;
                    while (curr < lti)
                    {
                        this.bw.WriteBits(2, 2U);
                        curr++; /* 10 */
                    }

                    while (curr > lti)
                    {
                        this.bw.WriteBits(2, 3U);
                        curr--; /* 11 */
                    }

                    this.bw.WriteBits(1, 0U);
                }
            }
        }


        private void sendMTFValues7(int nSelectors)
        {
            byte[][] len    = this.cstate.sendMTFValues_len;
            int[][] code    = this.cstate.sendMTFValues_code;
            byte[] selector = this.cstate.selector;
            char[] sfmap    = this.cstate.sfmap;
            int nMTFShadow  = this.nMTF;

            int selCtr = 0;

            for (int gs = 0; gs < nMTFShadow;)
            {
                int ge = Math.Min(gs + BZip2.G_SIZE - 1, nMTFShadow - 1);
                int ix = selector[selCtr] & 0xff;
                int[] code_selCtr = code[ix];
                byte[] len_selCtr = len[ix];

                while (gs <= ge)
                {
                    int sfmap_i = sfmap[gs];
                    int n = len_selCtr[sfmap_i] & 0xFF;
                    this.bw.WriteBits(n, (uint) code_selCtr[sfmap_i]);
                    gs++;
                }

                gs = ge + 1;
                selCtr++;
            }
        }

        private void moveToFrontCodeAndSend()
        {
            this.bw.WriteBits(24, (uint) this.origPtr);
            generateMTFValues();
            sendMTFValues();
        }






        private class CompressionState
        {
            // with blockSize 900k
            public readonly bool[] inUse = new bool[256]; // 256 byte
            public readonly byte[] unseqToSeq = new byte[256]; // 256 byte
            public readonly int[] mtfFreq = new int[BZip2.MaxAlphaSize]; // 1032 byte
            public readonly byte[] selector = new byte[BZip2.MaxSelectors]; // 18002 byte
            public readonly byte[] selectorMtf = new byte[BZip2.MaxSelectors]; // 18002 byte

            public readonly byte[] generateMTFValues_yy = new byte[256]; // 256 byte
            public byte[][] sendMTFValues_len;

            // byte
            public int[][] sendMTFValues_rfreq;

            // byte
            public readonly int[] sendMTFValues_fave = new int[BZip2.NGroups]; // 24 byte
            public readonly short[] sendMTFValues_cost = new short[BZip2.NGroups]; // 12 byte
            public int[][] sendMTFValues_code;

            // byte
            public readonly byte[] sendMTFValues2_pos = new byte[BZip2.NGroups]; // 6 byte
            public readonly bool[] sentMTFValues4_inUse16 = new bool[16]; // 16 byte

            public readonly int[] stack_ll = new int[BZip2.QSORT_STACK_SIZE]; // 4000 byte
            public readonly int[] stack_hh = new int[BZip2.QSORT_STACK_SIZE]; // 4000 byte
            public readonly int[] stack_dd = new int[BZip2.QSORT_STACK_SIZE]; // 4000 byte

            public readonly int[] mainSort_runningOrder = new int[256]; // 1024 byte
            public readonly int[] mainSort_copy = new int[256]; // 1024 byte
            public readonly bool[] mainSort_bigDone = new bool[256]; // 256 byte

            public int[] heap = new int[BZip2.MaxAlphaSize + 2]; // 1040 byte
            public int[] weight = new int[BZip2.MaxAlphaSize * 2]; // 2064 byte
            public int[] parent = new int[BZip2.MaxAlphaSize * 2]; // 2064 byte

            public readonly int[] ftab = new int[65537]; // 262148 byte
            // ------------
            // 333408 byte

            public byte[] block; // 900021 byte
            public int[] fmap; // 3600000 byte
            public char[] sfmap; // 3600000 byte

            // ------------
            // 8433529 byte
            // ============

            /**
             * Array instance identical to sfmap, both are used only
             * temporarily and independently, so we do not need to allocate
             * additional memory.
             */
            public char[] quadrant;

            public CompressionState(int blockSize100k)
            {
                int n = blockSize100k * BZip2.BlockSizeMultiple;
                this.block = new byte[(n + 1 + BZip2.NUM_OVERSHOOT_BYTES)];
                this.fmap = new int[n];
                this.sfmap = new char[2 * n];
                this.quadrant = this.sfmap;
                this.sendMTFValues_len = BZip2.InitRectangularArray<byte>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.sendMTFValues_rfreq = BZip2.InitRectangularArray<int>(BZip2.NGroups,BZip2.MaxAlphaSize);
                this.sendMTFValues_code = BZip2.InitRectangularArray<int>(BZip2.NGroups,BZip2.MaxAlphaSize);
            }

        }



    }
}