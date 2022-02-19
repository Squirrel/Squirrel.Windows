// from https://raw.githubusercontent.com/Azure/azure-storage-net/master/Lib/Common/Blob/SubStream.cs
// Apache License 2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Squirrel.Shared
{
    /// <summary>
    /// A wrapper class that creates a logical substream from a region within an existing seekable stream.
    /// Allows for concurrent, asynchronous read and seek operations on the wrapped stream.
    /// Ensures thread-safe operations between related substream instances via a shared, user-supplied synchronization mutex.
    /// This class will buffer read requests to minimize overhead on the underlying stream.
    /// </summary>
    public sealed class SubStream : Stream
    {
        // Stream to be logically wrapped.
        private Stream wrappedStream;

        // Position in the wrapped stream at which the SubStream should logically begin.
        private long streamBeginIndex;
        private readonly bool leaveOpen;

        // Total length of the substream.
        private long substreamLength;

        // Tracks the current position in the substream.
        private long substreamCurrentIndex;

        // Stream to manage read buffer, lazily initialized when read or seek operations commence.
        private Lazy<MemoryStream> readBufferStream;

        // Internal read buffer, lazily initialized when read or seek operations commence.
        private Lazy<byte[]> readBuffer;

        // Tracks the valid bytes remaining in the readBuffer
        private int readBufferLength;

        // Determines where to update the position of the readbuffer stream depending on the scenario)
        private bool shouldSeek = false;

        // Non-blocking semaphore for controlling read operations between related SubStream instances.
        public SemaphoreSlim Mutex { get; set; }

        public const int DefaultBufferSize = (int) (64 * 1024);
        public const int DefaultSubStreamBufferSize = (int) (4 * 1024 * 1024);

        // Current relative position in the substream.
        public override long Position {
            get {
                return this.substreamCurrentIndex;
            }

            set {
                AssertInBounds("Position", value, 0, this.substreamLength);

                // Check if we can potentially advance substream position without reallocating the read buffer.
                if (value >= this.substreamCurrentIndex) {
                    long offset = value - this.substreamCurrentIndex;

                    // New position is within the valid bytes stored in the readBuffer.
                    if (offset <= this.readBufferLength) {
                        this.readBufferLength -= (int) offset;
                        if (shouldSeek) {
                            this.readBufferStream.Value.Seek(offset, SeekOrigin.Current);
                        }
                    } else {
                        // Resets the read buffer.
                        this.readBufferLength = 0;
                        this.readBufferStream.Value.Seek(0, SeekOrigin.End);
                    }
                } else {
                    // Resets the read buffer.
                    this.readBufferLength = 0;
                    this.readBufferStream.Value.Seek(0, SeekOrigin.End);
                }

                this.substreamCurrentIndex = value;
            }
        }

        // Total length of the substream.
        public override long Length {
            get { return this.substreamLength; }
        }

        public override bool CanRead {
            get { return true; }
        }

        public override bool CanSeek {
            get { return true; }
        }

        public override bool CanWrite {
            get { return false; }
        }

        private void CheckDisposed()
        {
            if (this.wrappedStream == null) {
                throw new ObjectDisposedException("SubStreamWrapper");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!leaveOpen)
                this.wrappedStream.Dispose();

            this.wrappedStream = null;
            this.readBufferStream = null;
            this.readBuffer = null;
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        // Initiates the new buffer size to be used for read operations.
        public int ReadBufferSize {
            get {
                return this.readBuffer.Value.Length;
            }

            set {
                if (value < 2 * DefaultBufferSize) {
                    throw new ArgumentOutOfRangeException("ReadBufferSize", "ArgumentTooSmallError");
                }

                this.readBuffer = new Lazy<byte[]>(() => new byte[value]);
                this.readBufferStream = new Lazy<MemoryStream>(() => new MemoryStream(this.readBuffer.Value, 0, value, true));
                this.readBufferStream.Value.Seek(0, SeekOrigin.End);
            }
        }

        /// <summary>
        /// Creates a new SubStream instance.
        /// </summary>
        /// <param name="stream">A seekable source stream.</param>
        /// <param name="streamBeginIndex">The index in the wrapped stream where the logical SubStream should begin.</param>
        /// <param name="substreamLength">The length of the SubStream.</param>
        /// <param name="globalSemaphore"> A <see cref="SemaphoreSlim"/> object that is shared between related SubStream instances.</param>
        /// <param name="leaveOpen">Whether the underlying stream should be left open or not when this is disposed.</param>
        /// <remarks>
        /// The source stream to be wrapped must be seekable.
        /// The Semaphore object provided must have the initialCount thread parameter set to one to ensure only one concurrent request is granted at a time.
        /// </remarks>
        public SubStream(Stream stream, long streamBeginIndex, long substreamLength, SemaphoreSlim globalSemaphore, bool leaveOpen)
        {
            if (stream == null) {
                throw new ArgumentNullException("Stream.");
            } else if (!stream.CanSeek) {
                throw new NotSupportedException("Stream must be seekable.");
            } else if (globalSemaphore == null) {
                throw new ArgumentNullException("globalSemaphore");
            }

            AssertInBounds("streamBeginIndex", streamBeginIndex, 0, stream.Length);

            this.streamBeginIndex = streamBeginIndex;
            this.wrappedStream = stream;
            this.Mutex = globalSemaphore;
            this.leaveOpen = leaveOpen;
            this.substreamLength = Math.Min(substreamLength, stream.Length - streamBeginIndex);
            this.readBufferLength = 0;
            this.Position = 0;
            this.ReadBufferSize = DefaultSubStreamBufferSize;
        }

#if !(WINDOWS_RT || NETCORE)
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return AsApm(ReadAsync(buffer, offset, count, CancellationToken.None), callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            AssertNotNull("AsyncResult", asyncResult);
            return RunWithoutSynchronizationContext(() => ((Task<int>) asyncResult).Result);
        }
#endif

        /// <summary>
        /// Reads a block of bytes asynchronously from the substream read buffer.
        /// </summary>
        /// <param name="buffer">When this method returns, the buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
        /// <param name="count">The maximum number of bytes to be read.</param>
        /// <param name="cancellationToken">An object of type <see cref="CancellationToken"/> that propagates notification that operation should be canceled.</param>
        /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero if the end of the substream has been reached.</returns>
        /// <remarks>
        /// If the read request cannot be satisfied because the read buffer is empty or contains less than the requested number of the bytes, 
        /// the wrapped stream will be called to refill the read buffer.
        /// Only one read request to the underlying wrapped stream will be allowed at a time and concurrent requests will be queued up by effect of the shared semaphore mutex.
        /// </remarks>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            this.CheckDisposed();

            try {
                int readCount = this.CheckAdjustReadCount(count, offset, buffer.Length);
                int bytesRead = await this.readBufferStream.Value.ReadAsync(buffer, offset, Math.Min(readCount, this.readBufferLength), cancellationToken).ConfigureAwait(false);
                int bytesLeft = readCount - bytesRead;

                // must adjust readbufferLength
                this.shouldSeek = false;
                this.Position += bytesRead;

                if (bytesLeft > 0 && readBufferLength == 0) {
                    this.readBufferStream.Value.Position = 0;
                    int bytesAdded =
                        await this.ReadAsyncHelper(this.readBuffer.Value, 0, this.readBuffer.Value.Length, cancellationToken).ConfigureAwait(false);
                    this.readBufferLength = bytesAdded;
                    if (bytesAdded > 0) {
                        bytesLeft = Math.Min(bytesAdded, bytesLeft);
                        int secondRead = await this.readBufferStream.Value.ReadAsync(buffer, bytesRead + offset, bytesLeft, cancellationToken).ConfigureAwait(false);
                        bytesRead += secondRead;
                        this.Position += secondRead;
                    }
                }

                return bytesRead;
            } finally {
                this.shouldSeek = true;
            }
        }

        /// <summary>
        /// Reads a block of bytes from the wrapped stream asynchronously and writes the data to the SubStream buffer.
        /// </summary>
        /// <param name="buffer">When this method returns, the substream read buffer contains the specified byte array with the values between offset and (offset + count - 1) replaced by the bytes read from the current source.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin storing the data read from the current stream.</param>
        /// <param name="count">The maximum number of bytes to be read.</param>
        /// <param name="cancellationToken">An object of type <see cref="CancellationToken"/> that propagates notification that operation should be canceled.</param>
        /// <returns>The total number of bytes read into the buffer. This can be less than the number of bytes requested if that many bytes are not currently available, or zero if the end of the substream has been reached.</returns>
        /// <remarks>
        /// This method will allow only one read request to the underlying wrapped stream at a time, 
        /// while concurrent requests will be queued up by effect of the shared semaphore mutex.
        /// The caller is responsible for adjusting the substream position after a successful read.
        /// </remarks>
        private async Task<int> ReadAsyncHelper(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await this.Mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
            int result = 0;
            try {
                this.CheckDisposed();

                // Check if read is out of range and adjust to read only up to the substream bounds.
                count = this.CheckAdjustReadCount(count, offset, buffer.Length);

                // Only seek if wrapped stream is misaligned with the substream position.
                if (this.wrappedStream.Position != this.streamBeginIndex + this.Position) {
                    this.wrappedStream.Seek(this.streamBeginIndex + this.Position, SeekOrigin.Begin);
                }

                result = await this.wrappedStream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            } finally {
                this.Mutex.Release();
            }

            return result;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return RunWithoutSynchronizationContext(() => this.ReadAsync(buffer, offset, count).Result);
        }

        /// <summary>
        /// Sets the position within the current substream. 
        /// This operation does not perform a seek on the wrapped stream.
        /// </summary>
        /// <param name="offset">A byte offset relative to the origin parameter.</param>
        /// <param name="origin">A value of type System.IO.SeekOrigin indicating the reference point used to obtain the new position.</param>
        /// <returns>The new position within the current substream.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="offset"/> is invalid for SeekOrigin.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            this.CheckDisposed();
            long startIndex;

            // Map offset to the specified SeekOrigin of the substream.
            switch (origin) {
            case SeekOrigin.Begin:
                startIndex = 0;
                break;

            case SeekOrigin.Current:
                startIndex = this.Position;
                break;

            case SeekOrigin.End:
                startIndex = this.substreamLength;
                break;

            default:
                throw new ArgumentOutOfRangeException();
            }

            this.Position = startIndex + offset;
            return this.Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private int CheckAdjustReadCount(int count, int offset, int bufferLength)
        {
            if (offset < 0 || count < 0 || offset + count > bufferLength) {
                throw new ArgumentOutOfRangeException();
            }

            long currentPos = this.streamBeginIndex + this.Position;
            long endPos = this.streamBeginIndex + this.substreamLength;
            if (currentPos + count > endPos) {
                return (int) (endPos - currentPos);
            } else {
                return count;
            }
        }

        internal static void AssertInBounds<T>(string paramName, T val, T min, T max)
   where T : IComparable
        {
            if (val.CompareTo(min) < 0) {
                throw new ArgumentOutOfRangeException(paramName, "ArgumentTooSmallError");
            }

            if (val.CompareTo(max) > 0) {
                throw new ArgumentOutOfRangeException(paramName, "ArgumentTooLargeError");
            }
        }

        internal static void AssertNotNull(string paramName, object value)
        {
            if (value == null) {
                throw new ArgumentNullException(paramName);
            }
        }

        internal static void RunWithoutSynchronizationContext(Action actionToRun)
        {
            SynchronizationContext oldContext = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(null);
                actionToRun();
            } finally {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }

        internal static T RunWithoutSynchronizationContext<T>(Func<T> actionToRun)
        {
            SynchronizationContext oldContext = SynchronizationContext.Current;
            try {
                SynchronizationContext.SetSynchronizationContext(null);
                return actionToRun();
            } finally {
                SynchronizationContext.SetSynchronizationContext(oldContext);
            }
        }

        // Copied from https://msdn.microsoft.com/en-us/library/hh873178.aspx
        internal static IAsyncResult AsApm<T>(Task<T> task, AsyncCallback callback, object state)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(state);
            task.ContinueWith(t => {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);

                if (callback != null)
                    callback(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }
    }
}
