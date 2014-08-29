//#define Trace

// ParallelBZip2OutputStream.cs
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
// Last Saved: <2011-August-02 16:44:24>
//
// ------------------------------------------------------------------
//
// This module defines the ParallelBZip2OutputStream class, which is a
// BZip2 compressing stream. This code was derived in part from Apache
// commons source code. The license below applies to the original Apache
// code.
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
// compression as callers Write into it.  It is exactly the same in
// outward function as the BZip2OutputStream, except that this class can
// perform compression using multiple independent threads. Because of
// that, and because of the CPU-intensive nature of BZip2 compression,
// this class can perform significantly better (in terms of wall-click
// time) than the single-threaded variant, at the expense of memory and
// CPU utilization.
//
// BZip2 is a straightforward data format: there are 4 magic bytes at
// the top of the file, followed by 1 or more compressed blocks. There
// is a small "magic byte" trailer after all compressed blocks.
//
// In concept parallelizing BZip2 is simple: do the CPU-intensive
// compression for each block in a separate thread, then emit the
// compressed output, in order, to the output stream. Each block can be
// compressed independently, so a block is the natural candidate for the
// parcel of work that can be passed to an independent worker thread.
//
// The design approach used here is simple: within the Write() method of
// the stream, fill a block.  When the block is full, pass it to a
// background worker thread for compression.  When the compressor thread
// completes its work, the main thread (the application thread that
// calls Write()) can send the compressed data to the output stream,
// being careful to respect the order of the compressed blocks.
//
// The challenge of ordering the compressed data is a solved and
// well-understood problem - it is the same approach here as DotNetZip
// uses in the ParallelDeflateOutputStream. It is a map/reduce approach
// in design intent.
//
// One new twist for BZip2 is that the compressor output is not
// byte-aligned. In other words the final output of a compressed block
// will in general be a number of bits that is not a multiple of
// 8. Therefore, combining the ordered results of the N compressor
// threads requires additional byte-shredding by the parent
// stream. Hence this stream uses a BitWriter to adapt bit-oriented
// BZip2 output to the byte-oriented .NET Stream.
//
// The approach used here creates N instances of the BZip2Compressor
// type, where N is governed by the number of cores (cpus) and limited
// by the MaxWorkers property exposed by this class. Each
// BZip2Compressor instance gets its own MemoryStream, to which it
// writes its data, via a BitWriter.
//
// along with the bit accumulator described above. The MemoryStream
// would gather the byte-aligned compressed output of the compressor.

// When reducing the output of the various workers, this class must
// again do the byte-shredding thing. The data from the compressors is
// therefore shredded twice: once when being placed into the
// MemoryStream, and again when emitted into the final output stream
// that this class decorates. This is an unfortunate and seemingly
// unavoidable inefficiency. Two rounds of byte-shredding will use more
// CPU than we'd like, but I haven't imagined a way to avoid it.
//
// The BZip2Compressor is designed to write directly into the parent
// stream's accumulator (BitWriter) when possible, and write into a
// distinct BitWriter when necessary.  The former can be used in a
// single-thread scenario, while the latter is required in a
// multi-thread scenario.
//
// ----
//
// Regarding the Apache code base: Most of the code in this particular
// class is related to stream operations and thread synchronization, and
// is my own code. It largely does not rely on any code obtained from
// Apache commons. If you compare this code with the Apache commons
// BZip2OutputStream, you will see very little code that is common,
// except for the nearly-boilerplate structure that is common to all
// subtypes of System.IO.Stream. There may be some small remnants of
// code in this module derived from the Apache stuff, which is why I
// left the license in here. Most of the Apache commons compressor magic
// has been ported into the BZip2Compressor class.
//

using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace Ionic.BZip2
{
    internal class WorkItem
    {
        public int index;
        public BZip2Compressor Compressor { get; private set; }
        public MemoryStream ms;
        public int ordinal;
        public BitWriter bw;

        public WorkItem(int ix, int blockSize)
        {
            // compressed data gets written to a MemoryStream
            this.ms = new MemoryStream();
            this.bw = new BitWriter(ms);
            this.Compressor = new BZip2Compressor(bw, blockSize);
            this.index = ix;
        }
    }


    /// <summary>
    ///   A write-only decorator stream that compresses data as it is
    ///   written using the BZip2 algorithm. This stream compresses by
    ///   block using multiple threads.
    /// </summary>
    /// <para>
    ///   This class performs BZIP2 compression through writing.  For
    ///   more information on the BZIP2 algorithm, see
    ///   <see href="http://en.wikipedia.org/wiki/BZIP2"/>.
    /// </para>
    ///
    /// <para>
    ///   This class is similar to <see cref="Ionic.BZip2.BZip2OutputStream"/>,
    ///   except that this implementation uses an approach that employs multiple
    ///   worker threads to perform the compression.  On a multi-cpu or multi-core
    ///   computer, the performance of this class can be significantly higher than
    ///   the single-threaded BZip2OutputStream, particularly for larger streams.
    ///   How large?  Anything over 10mb is a good candidate for parallel
    ///   compression.
    /// </para>
    ///
    /// <para>
    ///   The tradeoff is that this class uses more memory and more CPU than the
    ///   vanilla <c>BZip2OutputStream</c>. Also, for small files, the
    ///   <c>ParallelBZip2OutputStream</c> can be much slower than the vanilla
    ///   <c>BZip2OutputStream</c>, because of the overhead associated to using the
    ///   thread pool.
    /// </para>
    ///
    /// <seealso cref="Ionic.BZip2.BZip2OutputStream" />
    public class ParallelBZip2OutputStream : System.IO.Stream
    {
        private static readonly int BufferPairsPerCore = 4;
        private int _maxWorkers;
        private bool firstWriteDone;
        private int lastFilled;
        private int lastWritten;
        private int latestCompressed;
        private int currentlyFilling;
        private volatile Exception pendingException;
        private bool handlingException;
        private bool emitting;
        private System.Collections.Generic.Queue<int>     toWrite;
        private System.Collections.Generic.Queue<int>     toFill;
        private System.Collections.Generic.List<WorkItem> pool;
        private object latestLock = new object();
        private object eLock = new object(); // for exceptions
        private object outputLock = new object(); // for multi-thread output
        private AutoResetEvent newlyCompressedBlob;

        long totalBytesWrittenIn;
        long totalBytesWrittenOut;
        bool leaveOpen;
        uint combinedCRC;
        Stream output;
        BitWriter bw;
        int blockSize100k;  // 0...9

        private TraceBits desiredTrace = TraceBits.Crc | TraceBits.Write;

        /// <summary>
        ///   Constructs a new <c>ParallelBZip2OutputStream</c>, that sends its
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
        ///           using (var compressor = new Ionic.BZip2.ParallelBZip2OutputStream(output))
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
        public ParallelBZip2OutputStream(Stream output)
            : this(output, BZip2.MaxBlockSize, false)
        {
        }

        /// <summary>
        ///   Constructs a new <c>ParallelBZip2OutputStream</c> with specified blocksize.
        /// </summary>
        /// <param name = "output">the destination stream.</param>
        /// <param name = "blockSize">
        ///   The blockSize in units of 100000 bytes.
        ///   The valid range is 1..9.
        /// </param>
        public ParallelBZip2OutputStream(Stream output, int blockSize)
            : this(output, blockSize, false)
        {
        }

        /// <summary>
        ///   Constructs a new <c>ParallelBZip2OutputStream</c>.
        /// </summary>
        ///   <param name = "output">the destination stream.</param>
        /// <param name = "leaveOpen">
        ///   whether to leave the captive stream open upon closing this stream.
        /// </param>
        public ParallelBZip2OutputStream(Stream output, bool leaveOpen)
            : this(output, BZip2.MaxBlockSize, leaveOpen)
        {
        }

        /// <summary>
        ///   Constructs a new <c>ParallelBZip2OutputStream</c> with specified blocksize,
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
        public ParallelBZip2OutputStream(Stream output, int blockSize, bool leaveOpen)
        {
            if (blockSize < BZip2.MinBlockSize || blockSize > BZip2.MaxBlockSize)
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
            this.leaveOpen = leaveOpen;
            this.combinedCRC = 0;
            this.MaxWorkers = 16; // default
            EmitHeader();
        }


        private void InitializePoolOfWorkItems()
        {
            this.toWrite = new Queue<int>();
            this.toFill = new Queue<int>();
            this.pool = new System.Collections.Generic.List<WorkItem>();
            int nWorkers = BufferPairsPerCore * Environment.ProcessorCount;
            nWorkers = Math.Min(nWorkers, this.MaxWorkers);
            for(int i=0; i < nWorkers; i++)
            {
                this.pool.Add(new WorkItem(i, this.blockSize100k));
                this.toFill.Enqueue(i);
            }

            this.newlyCompressedBlob = new AutoResetEvent(false);
            this.currentlyFilling = -1;
            this.lastFilled = -1;
            this.lastWritten = -1;
            this.latestCompressed = -1;
        }


        /// <summary>
        ///   The maximum number of concurrent compression worker threads to use.
        /// </summary>
        ///
        /// <remarks>
        /// <para>
        ///   This property sets an upper limit on the number of concurrent worker
        ///   threads to employ for compression. The implementation of this stream
        ///   employs multiple threads from the .NET thread pool, via <see
        ///   cref="System.Threading.ThreadPool.QueueUserWorkItem(WaitCallback)">
        ///   ThreadPool.QueueUserWorkItem()</see>, to compress the incoming data by
        ///   block.  As each block of data is compressed, this stream re-orders the
        ///   compressed blocks and writes them to the output stream.
        /// </para>
        ///
        /// <para>
        ///   A higher number of workers enables a higher degree of
        ///   parallelism, which tends to increase the speed of compression on
        ///   multi-cpu computers.  On the other hand, a higher number of buffer
        ///   pairs also implies a larger memory consumption, more active worker
        ///   threads, and a higher cpu utilization for any compression. This
        ///   property enables the application to limit its memory consumption and
        ///   CPU utilization behavior depending on requirements.
        /// </para>
        ///
        /// <para>
        ///   By default, DotNetZip allocates 4 workers per CPU core, subject to the
        ///   upper limit specified in this property. For example, suppose the
        ///   application sets this property to 16.  Then, on a machine with 2
        ///   cores, DotNetZip will use 8 workers; that number does not exceed the
        ///   upper limit specified by this property, so the actual number of
        ///   workers used will be 4 * 2 = 8.  On a machine with 4 cores, DotNetZip
        ///   will use 16 workers; again, the limit does not apply. On a machine
        ///   with 8 cores, DotNetZip will use 16 workers, because of the limit.
        /// </para>
        ///
        /// <para>
        ///   For each compression "worker thread" that occurs in parallel, there is
        ///   up to 2mb of memory allocated, for buffering and processing. The
        ///   actual number depends on the <see cref="BlockSize"/> property.
        /// </para>
        ///
        /// <para>
        ///   CPU utilization will also go up with additional workers, because a
        ///   larger number of buffer pairs allows a larger number of background
        ///   threads to compress in parallel. If you find that parallel
        ///   compression is consuming too much memory or CPU, you can adjust this
        ///   value downward.
        /// </para>
        ///
        /// <para>
        ///   The default value is 16. Different values may deliver better or
        ///   worse results, depending on your priorities and the dynamic
        ///   performance characteristics of your storage and compute resources.
        /// </para>
        ///
        /// <para>
        ///   The application can set this value at any time, but it is effective
        ///   only before the first call to Write(), which is when the buffers are
        ///   allocated.
        /// </para>
        /// </remarks>
        public int MaxWorkers
        {
            get
            {
                return _maxWorkers;
            }
            set
            {
                if (value < 4)
                    throw new ArgumentException("MaxWorkers",
                                                "Value must be 4 or greater.");
                _maxWorkers = value;
            }
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
            if (this.pendingException != null)
            {
                this.handlingException = true;
                var pe = this.pendingException;
                this.pendingException = null;
                throw pe;
            }

            if (this.handlingException)
                return;

            if (output == null)
                return;

            Stream o = this.output;

            try
            {
                FlushOutput(true);
            }
            finally
            {
                this.output = null;
                this.bw = null;
            }

            if (!leaveOpen)
                o.Close();
        }


        private void FlushOutput(bool lastInput)
        {
            if (this.emitting) return;

            // compress and write whatever is ready
            if (this.currentlyFilling >= 0)
            {
                WorkItem workitem = this.pool[this.currentlyFilling];
                CompressOne(workitem);
                this.currentlyFilling = -1; // get a new buffer next Write()
            }

            if (lastInput)
            {
                EmitPendingBuffers(true, false);
                EmitTrailer();
            }
            else
            {
                EmitPendingBuffers(false, false);
            }
        }



        /// <summary>
        ///   Flush the stream.
        /// </summary>
        public override void Flush()
        {
            if (this.output != null)
            {
                FlushOutput(false);
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

            TraceOutput(TraceBits.Write, "final total : {0} (0x{0:X})",
                        this.bw.TotalBytesWrittenOut);
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
        ///   Use the <c>ParallelBZip2OutputStream</c> to compress data while
        ///   writing: create a <c>ParallelBZip2OutputStream</c> with a writable
        ///   output stream.  Then call <c>Write()</c> on that
        ///   <c>ParallelBZip2OutputStream</c>, providing uncompressed data as
        ///   input.  The data sent to the output stream will be the compressed
        ///   form of the input data.
        /// </para>
        ///
        /// <para>
        ///   A <c>ParallelBZip2OutputStream</c> can be used only for
        ///   <c>Write()</c> not for <c>Read()</c>.
        /// </para>
        ///
        /// </remarks>
        ///
        /// <param name="buffer">The buffer holding data to write to the stream.</param>
        /// <param name="offset">the offset within that data array to find the first byte to write.</param>
        /// <param name="count">the number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            bool mustWait = false;

            // This method does this:
            //   0. handles any pending exceptions
            //   1. write any buffers that are ready to be written
            //   2. fills a compressor buffer; when full, flip state to 'Filled',
            //   3. if more data to be written,  goto step 1

            if (this.output == null)
                throw new IOException("the stream is not open");

            // dispense any exceptions that occurred on the BG threads
            if (this.pendingException != null)
            {
                this.handlingException = true;
                var pe = this.pendingException;
                this.pendingException = null;
                throw pe;
            }

            if (offset < 0)
                throw new IndexOutOfRangeException(String.Format("offset ({0}) must be > 0", offset));
            if (count < 0)
                throw new IndexOutOfRangeException(String.Format("count ({0}) must be > 0", count));
            if (offset + count > buffer.Length)
                throw new IndexOutOfRangeException(String.Format("offset({0}) count({1}) bLength({2})",
                                                                 offset, count, buffer.Length));


            if (count == 0) return;  // nothing to do


            if (!this.firstWriteDone)
            {
                // Want to do this on first Write, first session, and not in the
                // constructor.  Must allow the MaxWorkers to change after
                // construction, but before first Write().
                InitializePoolOfWorkItems();
                this.firstWriteDone = true;
            }

            int bytesWritten = 0;
            int bytesRemaining = count;

            do
            {
                // may need to make buffers available
                EmitPendingBuffers(false, mustWait);

                mustWait = false;

                // get a compressor to fill
                int ix = -1;
                if (this.currentlyFilling >= 0)
                {
                    ix = this.currentlyFilling;
                }
                else
                {
                    if (this.toFill.Count == 0)
                    {
                        // No compressors available to fill, so... need to emit
                        // compressed buffers.
                        mustWait = true;
                        continue;
                    }

                    ix = this.toFill.Dequeue();
                    ++this.lastFilled;
                }

                WorkItem workitem = this.pool[ix];
                workitem.ordinal = this.lastFilled;

                int n = workitem.Compressor.Fill(buffer, offset, bytesRemaining);
                if (n != bytesRemaining)
                {
                    if (!ThreadPool.QueueUserWorkItem( CompressOne, workitem ))
                        throw new Exception("Cannot enqueue workitem");

                    this.currentlyFilling = -1; // will get a new buffer next time
                    offset += n;
                }
                else
                    this.currentlyFilling = ix;

                bytesRemaining -= n;
                bytesWritten += n;
            }
            while (bytesRemaining > 0);

            totalBytesWrittenIn += bytesWritten;
            return;
        }



        private void EmitPendingBuffers(bool doAll, bool mustWait)
        {
            // When combining parallel compression with a ZipSegmentedStream, it's
            // possible for the ZSS to throw from within this method.  In that
            // case, Close/Dispose will be called on this stream, if this stream
            // is employed within a using or try/finally pair as required. But
            // this stream is unaware of the pending exception, so the Close()
            // method invokes this method AGAIN. This can lead to a deadlock.
            // Therefore, failfast if re-entering.

            if (emitting) return;
            emitting = true;

            if (doAll || mustWait)
                this.newlyCompressedBlob.WaitOne();

            do
            {
                int firstSkip = -1;
                int millisecondsToWait = doAll ? 200 : (mustWait ? -1 : 0);
                int nextToWrite = -1;

                do
                {
                    if (Monitor.TryEnter(this.toWrite, millisecondsToWait))
                    {
                        nextToWrite = -1;
                        try
                        {
                            if (this.toWrite.Count > 0)
                                nextToWrite = this.toWrite.Dequeue();
                        }
                        finally
                        {
                            Monitor.Exit(this.toWrite);
                        }

                        if (nextToWrite >= 0)
                        {
                            WorkItem workitem = this.pool[nextToWrite];
                            if (workitem.ordinal != this.lastWritten + 1)
                            {
                                // out of order. requeue and try again.
                                lock(this.toWrite)
                                {
                                    this.toWrite.Enqueue(nextToWrite);
                                }

                                if (firstSkip == nextToWrite)
                                {
                                    // We went around the list once.
                                    // None of the items in the list is the one we want.
                                    // Now wait for a compressor to signal again.
                                    this.newlyCompressedBlob.WaitOne();
                                    firstSkip = -1;
                                }
                                else if (firstSkip == -1)
                                    firstSkip = nextToWrite;

                                continue;
                            }

                            firstSkip = -1;

                            TraceOutput(TraceBits.Write,
                                        "Writing block {0}", workitem.ordinal);

                            // write the data to the output
                            var bw2 = workitem.bw;
                            bw2.Flush(); // not bw2.FinishAndPad()!
                            var ms = workitem.ms;
                            ms.Seek(0,SeekOrigin.Begin);

                            // cannot dump bytes!!
                            // ms.WriteTo(this.output);
                            //
                            // must do byte shredding:
                            int n;
                            int y = -1;
                            long totOut = 0;
                            var buffer = new byte[1024];
                            while ((n = ms.Read(buffer,0,buffer.Length)) > 0)
                            {
#if Trace
                                if (y == -1) // diagnostics only
                                {
                                    var sb1 = new System.Text.StringBuilder();
                                    sb1.Append("first 16 whole bytes in block: ");
                                    for (int z=0; z < 16; z++)
                                        sb1.Append(String.Format(" {0:X2}", buffer[z]));
                                    TraceOutput(TraceBits.Write, sb1.ToString());
                                }
#endif
                                y = n;
                                for (int k=0; k < n; k++)
                                {
                                    this.bw.WriteByte(buffer[k]);
                                }
                                totOut += n;
                            }
#if Trace
                            TraceOutput(TraceBits.Write,"out block length (bytes): {0} (0x{0:X})", totOut);
                            var sb = new System.Text.StringBuilder();
                            sb.Append("final 16 whole bytes in block: ");
                            for (int z=0; z < 16; z++)
                                sb.Append(String.Format(" {0:X2}", buffer[y-1-12+z]));
                            TraceOutput(TraceBits.Write, sb.ToString());
#endif

                            // and now any remaining bits
                            TraceOutput(TraceBits.Write,
                                        " remaining bits: {0} 0x{1:X}",
                                        bw2.NumRemainingBits,
                                        bw2.RemainingBits);
                            if (bw2.NumRemainingBits > 0)
                            {
                                this.bw.WriteBits(bw2.NumRemainingBits, bw2.RemainingBits);
                            }

                            TraceOutput(TraceBits.Crc," combined CRC (before): {0:X8}",
                                        this.combinedCRC);
                            this.combinedCRC = (this.combinedCRC << 1) | (this.combinedCRC >> 31);
                            this.combinedCRC ^= (uint) workitem.Compressor.Crc32;

                            TraceOutput(TraceBits.Crc,
                                        " block    CRC         : {0:X8}",
                                        workitem.Compressor.Crc32);
                            TraceOutput(TraceBits.Crc,
                                        " combined CRC (after) : {0:X8}",
                                        this.combinedCRC);
                            TraceOutput(TraceBits.Write,
                                        "total written out: {0} (0x{0:X})",
                                        this.bw.TotalBytesWrittenOut);
                            TraceOutput(TraceBits.Write | TraceBits.Crc, "");

                            this.totalBytesWrittenOut += totOut;

                            bw2.Reset();
                            this.lastWritten = workitem.ordinal;
                            workitem.ordinal = -1;
                            this.toFill.Enqueue(workitem.index);

                            // don't wait next time through
                            if (millisecondsToWait == -1) millisecondsToWait = 0;
                        }
                    }
                    else
                        nextToWrite = -1;

                } while (nextToWrite >= 0);

            } while (doAll && (this.lastWritten != this.latestCompressed));

            if (doAll)
            {
                TraceOutput(TraceBits.Crc,
                            " combined CRC (final) : {0:X8}", this.combinedCRC);
            }

            emitting = false;
        }


        private void CompressOne(Object wi)
        {
            // compress and one buffer
            WorkItem workitem = (WorkItem) wi;
            try
            {
                // compress and write to the compressor's MemoryStream
                workitem.Compressor.CompressAndWrite();

                lock(this.latestLock)
                {
                    if (workitem.ordinal > this.latestCompressed)
                        this.latestCompressed = workitem.ordinal;
                }
                lock (this.toWrite)
                {
                    this.toWrite.Enqueue(workitem.index);
                }
                this.newlyCompressedBlob.Set();
            }
            catch (System.Exception exc1)
            {
                lock(this.eLock)
                {
                    // expose the exception to the main thread
                    if (this.pendingException!=null)
                        this.pendingException = exc1;
                }
            }
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
        /// The return value depends on whether the captive stream supports writing.
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
        /// The total number of bytes written out by the stream.
        /// </summary>
        /// <remarks>
        /// This value is meaningful only after a call to Close().
        /// </remarks>
        public Int64 BytesWrittenOut { get { return totalBytesWrittenOut; } }

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
        /// Calling this method always throws a <see cref="NotImplementedException"/>.
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
                lock(outputLock)
                {
                    int tid = Thread.CurrentThread.GetHashCode();
#if !SILVERLIGHT
                    Console.ForegroundColor = (ConsoleColor) (tid % 8 + 10);
#endif
                    Console.Write("{0:000} PBOS ", tid);
                    Console.WriteLine(format, varParams);
#if !SILVERLIGHT
                    Console.ResetColor();
#endif
                }
            }
        }

    }

}
