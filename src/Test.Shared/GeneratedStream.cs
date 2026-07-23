namespace Test.Shared
{
    using System;
    using System.IO;

    /// <summary>
    /// A read-only, non-seekable stream that yields a deterministic byte pattern of a fixed length without
    /// materializing it in memory. Used to exercise large streamed payloads. The byte at absolute position
    /// <c>i</c> is <c>(byte)(i % 251)</c>.
    /// </summary>
    public sealed class GeneratedStream : Stream
    {
        #region Public-Members

        /// <summary>
        /// Always true.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Always false.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Always false.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// Total length in bytes.
        /// </summary>
        public override long Length => _Length;

        /// <summary>
        /// Current position.
        /// </summary>
        public override long Position
        {
            get { return _Position; }
            set { throw new NotSupportedException(); }
        }

        #endregion

        #region Private-Members

        private readonly long _Length;
        private long _Position;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a generated stream.
        /// </summary>
        /// <param name="length">Number of bytes to yield; zero or greater.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
        public GeneratedStream(long length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _Length = length;
            _Position = 0;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compute the pattern byte for an absolute position.
        /// </summary>
        /// <param name="position">Absolute position.</param>
        /// <returns>The pattern byte.</returns>
        public static byte ByteAt(long position)
        {
            return (byte)(position % 251);
        }

        /// <summary>
        /// Read the next bytes of the pattern.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">Offset into the buffer.</param>
        /// <param name="count">Maximum bytes to read.</param>
        /// <returns>Bytes read, or zero at end.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _Length - _Position;
            if (remaining <= 0) return 0;

            int toWrite = (int)Math.Min(count, remaining);
            for (int i = 0; i < toWrite; i++)
            {
                buffer[offset + i] = ByteAt(_Position + i);
            }

            _Position += toWrite;
            return toWrite;
        }

        /// <summary>
        /// No-op.
        /// </summary>
        public override void Flush()
        {
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="offset">Ignored.</param>
        /// <param name="origin">Ignored.</param>
        /// <returns>Never returns.</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="value">Ignored.</param>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="buffer">Ignored.</param>
        /// <param name="offset">Ignored.</param>
        /// <param name="count">Ignored.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        #endregion
    }
}
