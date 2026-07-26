namespace PepperX.Core.Caching
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A read-only, forward pass-through stream that tees the bytes read from a source into an in-memory
    /// buffer, up to a capture limit, in a single pass. It lets a write path stream a payload to durable
    /// storage while simultaneously capturing it for the cache without a second read. If the source yields
    /// more than the limit, the buffer is discarded and capture is marked overflowed, so an over-ceiling
    /// object never lingers in memory.
    /// </summary>
    /// <remarks>This stream is not thread-safe; use one instance per write. It does not dispose the source.</remarks>
    public sealed class BoundedCaptureStream : Stream
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
        /// Not supported.
        /// </summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Length => throw new NotSupportedException();

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        #endregion

        #region Private-Members

        private readonly Stream _Source;
        private readonly long _CaptureLimit;
        private readonly MemoryStream _Buffer = new MemoryStream();
        private bool _Overflowed = false;
        private long _TotalRead = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a capture stream over a source.
        /// </summary>
        /// <param name="source">Source stream, read forward to completion by the consumer.</param>
        /// <param name="captureLimit">Maximum number of bytes to retain; a source larger than this overflows
        /// and is not captured. Must be positive.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="captureLimit"/> is not positive.</exception>
        public BoundedCaptureStream(Stream source, long captureLimit)
        {
            _Source = source ?? throw new ArgumentNullException(nameof(source));
            if (captureLimit <= 0) throw new ArgumentOutOfRangeException(nameof(captureLimit));
            _CaptureLimit = captureLimit;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Retrieve the captured payload when the source did not exceed the capture limit.
        /// </summary>
        /// <param name="payload">The captured bytes when this returns true; otherwise an empty array.</param>
        /// <returns>True when the full payload was captured; false when it overflowed the limit.</returns>
        public bool TryGetCapturedPayload(out byte[] payload)
        {
            if (_Overflowed)
            {
                payload = Array.Empty<byte>();
                return false;
            }

            payload = _Buffer.ToArray();
            return true;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _Source.Read(buffer, offset, count);
            if (read > 0) Capture(new ReadOnlySpan<byte>(buffer, offset, read));
            return read;
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _Source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) Capture(buffer.Span.Slice(0, read));
            return read;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose the capture buffer. The source is not disposed.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing) _Buffer.Dispose();
            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private void Capture(ReadOnlySpan<byte> bytes)
        {
            if (_Overflowed) return;

            _TotalRead += bytes.Length;
            if (_TotalRead > _CaptureLimit)
            {
                _Overflowed = true;
                _Buffer.SetLength(0);
                _Buffer.Capacity = 0;
                return;
            }

            _Buffer.Write(bytes);
        }

        #endregion
    }
}
