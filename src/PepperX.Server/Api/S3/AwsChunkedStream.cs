namespace PepperX.Server.Api.S3
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Decodes an AWS streaming-signature body (<c>Content-Encoding: aws-chunked</c>,
    /// <c>x-amz-content-sha256: STREAMING-AWS4-HMAC-SHA256-PAYLOAD</c>) into the underlying object payload.
    /// Each chunk is framed as <c>hexSize[;chunk-signature=...]CRLF &lt;data&gt; CRLF</c>, terminated by a
    /// zero-length chunk and an optional trailer. This stream yields only the <c>&lt;data&gt;</c> bytes. It is
    /// forward-only and not thread-safe.
    /// </summary>
    public sealed class AwsChunkedStream : Stream
    {
        #region Public-Members

        /// <summary>Always true.</summary>
        public override bool CanRead => true;

        /// <summary>Always false.</summary>
        public override bool CanSeek => false;

        /// <summary>Always false.</summary>
        public override bool CanWrite => false;

        /// <summary>Not supported.</summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>Not supported.</summary>
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        #endregion

        #region Private-Members

        private readonly Stream _Source;
        private long _ChunkRemaining;
        private bool _Finished;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the decoder over a framed source stream.
        /// </summary>
        /// <param name="source">The framed request body stream.</param>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
        public AwsChunkedStream(Stream source)
        {
            _Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read decoded payload bytes.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">Offset into the buffer.</param>
        /// <param name="count">Maximum bytes to read.</param>
        /// <returns>Bytes read, or zero at end.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously read decoded payload bytes.
        /// </summary>
        /// <param name="buffer">Destination memory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Bytes read, or zero at end.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_Finished) return 0;

            if (_ChunkRemaining == 0)
            {
                long size = await ReadChunkSizeAsync(cancellationToken).ConfigureAwait(false);
                if (size == 0)
                {
                    _Finished = true;
                    return 0;
                }
                _ChunkRemaining = size;
            }

            int toRead = (int)Math.Min(buffer.Length, _ChunkRemaining);
            int read = await _Source.ReadAsync(buffer.Slice(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                _Finished = true;
                return 0;
            }

            _ChunkRemaining -= read;
            if (_ChunkRemaining == 0) await ConsumeCrlfAsync(cancellationToken).ConfigureAwait(false);
            return read;
        }

        /// <summary>Not supported.</summary>
        public override void Flush()
        {
        }

        /// <summary>Not supported.</summary>
        /// <param name="offset">Ignored.</param>
        /// <param name="origin">Ignored.</param>
        /// <returns>Never returns.</returns>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>Not supported.</summary>
        /// <param name="value">Ignored.</param>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Not supported.</summary>
        /// <param name="buffer">Ignored.</param>
        /// <param name="offset">Ignored.</param>
        /// <param name="count">Ignored.</param>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion

        #region Private-Methods

        private async Task<long> ReadChunkSizeAsync(CancellationToken token)
        {
            string line = await ReadLineAsync(token).ConfigureAwait(false);
            int semicolon = line.IndexOf(';');
            string hex = semicolon >= 0 ? line.Substring(0, semicolon) : line;
            hex = hex.Trim();
            if (hex.Length == 0) return 0;
            return Convert.ToInt64(hex, 16);
        }

        private async Task ConsumeCrlfAsync(CancellationToken token)
        {
            await ReadByteStrictAsync(token).ConfigureAwait(false);
            await ReadByteStrictAsync(token).ConfigureAwait(false);
        }

        private async Task<string> ReadLineAsync(CancellationToken token)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            byte[] one = new byte[1];
            while (true)
            {
                int read = await _Source.ReadAsync(one.AsMemory(0, 1), token).ConfigureAwait(false);
                if (read == 0) break;
                if (one[0] == (byte)'\r') continue;
                if (one[0] == (byte)'\n') break;
                sb.Append((char)one[0]);
            }
            return sb.ToString();
        }

        private async Task ReadByteStrictAsync(CancellationToken token)
        {
            byte[] one = new byte[1];
            await _Source.ReadAsync(one.AsMemory(0, 1), token).ConfigureAwait(false);
        }

        #endregion
    }
}
