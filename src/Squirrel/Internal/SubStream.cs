using System;
using System.IO;

namespace Squirrel
{
    internal class SubStream : Stream
    {
        private readonly Stream _wrappedStream;
        private readonly long _startOffset;

        public SubStream(Stream wrappedStream, long startOffset)
        {
            _wrappedStream = wrappedStream;
            _startOffset = startOffset;

            if (startOffset >= wrappedStream.Length)
                throw new ArgumentException("Offset+Length must be less than or equal to the length of the wrapped stream");

            Seek(0, SeekOrigin.Begin);
        }

        public override bool CanRead => _wrappedStream.CanRead;

        public override bool CanSeek => _wrappedStream.CanSeek;

        public override bool CanWrite => _wrappedStream.CanWrite;

        public override long Length => _wrappedStream.Length - _startOffset;

        public override long Position {
            get => _wrappedStream.Position - _startOffset;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
            _wrappedStream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _wrappedStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (offset > Length)
                throw new ArgumentException("Offset can not be greater than stream length");

            if (origin == SeekOrigin.Begin) {
                return _wrappedStream.Seek(_startOffset + offset, SeekOrigin.Begin) - _startOffset;
            } else if (origin == SeekOrigin.End) {
                return _wrappedStream.Seek(offset, SeekOrigin.End) - _startOffset;
            }

            var newPosition = _wrappedStream.Seek(offset, SeekOrigin.Current);
            if (newPosition < _startOffset)
                throw new ArgumentException("Cannot seak beyond the beginning of a stream");

            return newPosition - _startOffset;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _wrappedStream.Write(buffer, offset, count);
        }
    }
}
