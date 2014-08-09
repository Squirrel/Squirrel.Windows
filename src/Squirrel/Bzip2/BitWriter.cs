// BitWriter.cs
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
// Last Saved: <2011-July-25 18:57:31>
//
// ------------------------------------------------------------------
//
// This module defines the BitWriter class, which writes bits at a time
// to an output stream. It's used by the BZip2Compressor class, and by
// the BZip2OutputStream class and its parallel variant,
// ParallelBZip2OutputStream.
//
// ------------------------------------------------------------------

//
// Design notes:
//
// BZip2 employs byte-shredding in its data format - rather than
// aligning all data items in a compressed .bz2 file on byte barriers,
// the BZip2 format uses portions of bytes to represent independent
// pieces of information. This "shredding" starts with the first
// "randomised" bit - just 12 bytes or so into a bz2 file or stream. But
// the approach is used extensively in bzip2 files - sometimes 5 bits
// are used, sometimes 24 or 3 bits, sometimes just 1 bit, and so on.
// It's not possible to send this information directly to a stream in
// this form; Streams in .NET accept byte-oriented input.  Therefore,
// when actually writing a bz2 file, the output data must be organized
// into a byte-aligned format before being written to the output stream.
//
// This BitWriter class provides the byte-shredding necessary for BZip2
// output. Think of this class as an Adapter that enables Bit-oriented
// output to a standard byte-oriented .NET stream. This class writes
// data out to the captive output stream only after the data bits have
// been accumulated and aligned. For example, suppose that during
// operation, the BZip2 compressor emits 5 bits, then 24 bits, then 32
// bits.  When the first 5 bits are sent to the BitWriter, nothing is
// written to the output stream; instead these 5 bits are simply stored
// in the internal accumulator.  When the next 24 bits are written, the
// first 3 bits are gathered with the accumulated bits. The resulting
// 5+3 constitutes an entire byte; the BitWriter then actually writes
// that byte to the output stream. This leaves 21 bits. BitWriter writes
// 2 more whole bytes (16 more bits), in 8-bit chunks, leaving 5 in the
// accumulator. BitWriter then follows the same procedure with the 32
// new bits. And so on.
//
// A quick tour of the implementation:
//
// The accumulator is a uint - so it can accumulate at most 4 bytes of
// information. In practice because of the design of this class, it
// never accumulates more than 3 bytes.
//
// The Flush() method emits all whole bytes available. After calling
// Flush(), there may be between 0-7 bits yet to be emitted into the
// output stream.
//
// FinishAndPad() emits all data, including the last partial byte and
// any necessary padding. In effect, it establishes a byte-alignment
// barrier. To support bzip2, FinishAndPad() should be called only once
// for a bz2 file, after the last bit of data has been written through
// this adapter.  Other binary file formats may use byte-alignment at
// various points within the file, and FinishAndPad() would support that
// scenario.
//
// The internal fn Reset() is used to reset the state of the adapter;
// this class is used by BZip2Compressor, instances of which get re-used
// by multiple distinct threads, for different blocks of data.
//


using System;
using System.IO;

namespace Ionic.BZip2
{

    internal class BitWriter
    {
        uint accumulator;
        int nAccumulatedBits;
        Stream output;
        int totalBytesWrittenOut;

        public BitWriter(Stream s)
        {
            this.output = s;
        }

        /// <summary>
        ///   Delivers the remaining bits, left-aligned, in a byte.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This is valid only if NumRemainingBits is less than 8;
        ///     in other words it is valid only after a call to Flush().
        ///   </para>
        /// </remarks>
        public byte RemainingBits
        {
            get
            {
                return (byte) (this.accumulator >> (32 - this.nAccumulatedBits) & 0xff);
            }
        }

        public int NumRemainingBits
        {
            get
            {
                return this.nAccumulatedBits;
            }
        }

        public int TotalBytesWrittenOut
        {
            get
            {
                return this.totalBytesWrittenOut;
            }
        }

        /// <summary>
        ///   Reset the BitWriter.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This is useful when the BitWriter writes into a MemoryStream, and
        ///     is used by a BZip2Compressor, which itself is re-used for multiple
        ///     distinct data blocks.
        ///   </para>
        /// </remarks>
        public void Reset()
        {
            this.accumulator = 0;
            this.nAccumulatedBits = 0;
            this.totalBytesWrittenOut = 0;
            this.output.Seek(0, SeekOrigin.Begin);
            this.output.SetLength(0);
        }

        /// <summary>
        ///   Write some number of bits from the given value, into the output.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     The nbits value should be a max of 25, for safety. For performance
        ///     reasons, this method does not check!
        ///   </para>
        /// </remarks>
        public void WriteBits(int nbits, uint value)
        {
            int nAccumulated = this.nAccumulatedBits;
            uint u = this.accumulator;

            while (nAccumulated >= 8)
            {
                this.output.WriteByte ((byte)(u >> 24 & 0xff));
                this.totalBytesWrittenOut++;
                u <<= 8;
                nAccumulated -= 8;
            }

            this.accumulator = u | (value << (32 - nAccumulated - nbits));
            this.nAccumulatedBits = nAccumulated + nbits;

            // Console.WriteLine("WriteBits({0}, 0x{1:X2}) => {2:X8} n({3})",
            //                   nbits, value, accumulator, nAccumulatedBits);
            // Console.ReadLine();

            // At this point the accumulator may contain up to 31 bits waiting for
            // output.
        }


        /// <summary>
        ///   Write a full 8-bit byte into the output.
        /// </summary>
        public void WriteByte(byte b)
        {
            WriteBits(8, b);
        }

        /// <summary>
        ///   Write four 8-bit bytes into the output.
        /// </summary>
        public void WriteInt(uint u)
        {
            WriteBits(8, (u >> 24) & 0xff);
            WriteBits(8, (u >> 16) & 0xff);
            WriteBits(8, (u >> 8) & 0xff);
            WriteBits(8, u & 0xff);
        }

        /// <summary>
        ///   Write all available byte-aligned bytes.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     This method writes no new output, but flushes any accumulated
        ///     bits. At completion, the accumulator may contain up to 7
        ///     bits.
        ///   </para>
        ///   <para>
        ///     This is necessary when re-assembling output from N independent
        ///     compressors, one for each of N blocks. The output of any
        ///     particular compressor will in general have some fragment of a byte
        ///     remaining. This fragment needs to be accumulated into the
        ///     parent BZip2OutputStream.
        ///   </para>
        /// </remarks>
        public void Flush()
        {
            WriteBits(0,0);
        }


        /// <summary>
        ///   Writes all available bytes, and emits padding for the final byte as
        ///   necessary. This must be the last method invoked on an instance of
        ///   BitWriter.
        /// </summary>
        public void FinishAndPad()
        {
            Flush();

            if (this.NumRemainingBits > 0)
            {
                byte b = (byte)((this.accumulator >> 24) & 0xff);
                this.output.WriteByte(b);
                this.totalBytesWrittenOut++;
            }
        }

    }

}