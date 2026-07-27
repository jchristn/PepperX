namespace PepperX.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A read-only, forward-only stream that concatenates a sequence of source streams opened lazily, in
    /// order. Used to assemble a multipart upload's staged parts into a single payload without buffering
    /// the whole object in memory. Each source is opened when first needed and disposed once exhausted.
    /// This type is not thread-safe: a single consumer must read it sequentially.
    /// </summary>
    public sealed class ConcatReadStream : Stream
    {
        #region Public-Members

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => _Length;

        /// <inheritdoc />
        public override long Position
        {
            get
            {
                return _Position;
            }
            set
            {
                throw new NotSupportedException("ConcatReadStream is forward-only.");
            }
        }

        #endregion

        #region Private-Members

        private readonly IReadOnlyList<Func<CancellationToken, Task<Stream>>> _Openers;
        private readonly long _Length;
        private int _Index = 0;
        private Stream? _Current = null;
        private long _Position = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a concatenating read stream.
        /// </summary>
        /// <param name="openers">Ordered source-stream factories, invoked lazily as reading advances.</param>
        /// <param name="length">Total length across all sources; zero or greater.</param>
        /// <exception cref="ArgumentNullException"><paramref name="openers"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
        public ConcatReadStream(IReadOnlyList<Func<CancellationToken, Task<Stream>>> openers, long length)
        {
            if (openers == null) throw new ArgumentNullException(nameof(openers));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _Openers = openers;
            _Length = length;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(ConcatReadStream));

            while (true)
            {
                if (_Current == null)
                {
                    if (_Index >= _Openers.Count) return 0;
                    _Current = await _Openers[_Index].Invoke(cancellationToken).ConfigureAwait(false);
                    _Index++;
                }

                int read = await _Current.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read > 0)
                {
                    _Position += read;
                    return read;
                }

                await _Current.DisposeAsync().ConfigureAwait(false);
                _Current = null;
            }
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("ConcatReadStream is forward-only.");
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException("ConcatReadStream is read-only.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("ConcatReadStream is read-only.");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing && _Current != null)
                {
                    _Current.Dispose();
                    _Current = null;
                }
                _Disposed = true;
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
