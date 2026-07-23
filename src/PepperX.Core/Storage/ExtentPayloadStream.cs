namespace PepperX.Core.Storage
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Storage.Format;

    /// <summary>
    /// A read-only, forward stream over the payload region of an extent file, together with the parsed
    /// header. Reading past the payload window returns end-of-stream. Disposing this closes the underlying
    /// file. This type is not thread-safe; use one instance per read.
    /// </summary>
    public sealed class ExtentPayloadStream : Stream
    {
        #region Public-Members

        /// <summary>
        /// The parsed extent header.
        /// </summary>
        public ExtentHeader Header
        {
            get
            {
                return _Header;
            }
        }

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
        /// The number of payload bytes exposed by this stream.
        /// </summary>
        public override long Length => _WindowLength;

        /// <summary>
        /// The current read position within the payload window.
        /// </summary>
        public override long Position
        {
            get
            {
                return _Consumed;
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        #endregion

        #region Private-Members

        private readonly Stream _Inner;
        private readonly ExtentHeader _Header;
        private readonly long _WindowLength;
        private long _Consumed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a payload stream over an already-positioned underlying stream.
        /// </summary>
        /// <param name="inner">Underlying stream, positioned at the start of the payload window.</param>
        /// <param name="header">Parsed extent header.</param>
        /// <param name="windowLength">Number of payload bytes to expose; zero or greater.</param>
        /// <exception cref="ArgumentNullException"><paramref name="inner"/> or <paramref name="header"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="windowLength"/> is negative.</exception>
        public ExtentPayloadStream(Stream inner, ExtentHeader header, long windowLength)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _Header = header ?? throw new ArgumentNullException(nameof(header));
            if (windowLength < 0) throw new ArgumentOutOfRangeException(nameof(windowLength));
            _WindowLength = windowLength;
            _Consumed = 0;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read from the payload window.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">Offset into the buffer.</param>
        /// <param name="count">Maximum bytes to read.</param>
        /// <returns>Bytes read, or zero at end of window.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int allowed = ClampCount(count);
            if (allowed == 0) return 0;
            int read = _Inner.Read(buffer, offset, allowed);
            _Consumed += read;
            return read;
        }

        /// <summary>
        /// Asynchronously read from the payload window.
        /// </summary>
        /// <param name="buffer">Destination memory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes read, or zero at end of window.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int allowed = ClampCount(buffer.Length);
            if (allowed == 0) return 0;
            int read = await _Inner.ReadAsync(buffer.Slice(0, allowed), cancellationToken).ConfigureAwait(false);
            _Consumed += read;
            return read;
        }

        /// <summary>
        /// Not supported.
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

        #region Protected-Methods

        /// <summary>
        /// Dispose the underlying stream.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) _Inner.Dispose();
            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private int ClampCount(int count)
        {
            long remaining = _WindowLength - _Consumed;
            if (remaining <= 0) return 0;
            return (int)Math.Min(count, remaining);
        }

        #endregion
    }
}
