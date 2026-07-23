namespace PepperX.Core.Storage.Format
{
    using System;
    using System.Buffers.Binary;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Serialization;

    /// <summary>
    /// Writes the PXE1 extent file format. The header must already carry the payload size and checksum
    /// (computed by the caller in a prior streaming pass). This type is thread-safe.
    /// </summary>
    public static class ExtentFormatWriter
    {
        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private static readonly int _CopyBufferBytes = 81920;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write a complete extent (prefix, header, payload, trailer) to a target stream.
        /// </summary>
        /// <param name="target">Destination stream, positioned where the extent should begin.</param>
        /// <param name="header">Complete header, including <see cref="ExtentHeader.SizeBytes"/> and <see cref="ExtentHeader.Sha256"/>.</param>
        /// <param name="payload">Payload source, positioned at its start, containing exactly <see cref="ExtentHeader.SizeBytes"/> bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">The header checksum is not a 32-byte hex value.</exception>
        /// <exception cref="EndOfStreamException">The payload is shorter than the declared size.</exception>
        public static async Task WriteAsync(Stream target, ExtentHeader header, Stream payload, CancellationToken token = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (payload == null) throw new ArgumentNullException(nameof(payload));

            byte[] hashBytes;
            try
            {
                hashBytes = Convert.FromHexString(header.Sha256);
            }
            catch (FormatException ex)
            {
                throw new ArgumentException("Header checksum must be a hex string.", nameof(header), ex);
            }

            if (hashBytes.Length != ExtentFormatConstants.HashLength)
                throw new ArgumentException("Header checksum must be 32 bytes.", nameof(header));

            string json = _Serializer.SerializeJson(header) ?? "{}";
            byte[] headerJson = Encoding.UTF8.GetBytes(json);

            byte[] prefix = new byte[ExtentFormatConstants.PrefixLength];
            ExtentFormatConstants.Magic.CopyTo(prefix.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(4, 2), ExtentFormatConstants.FormatVersion);
            ushort flags = header.Object != null ? ExtentFormatConstants.FlagHasMetadataObject : (ushort)0;
            BinaryPrimitives.WriteUInt16LittleEndian(prefix.AsSpan(6, 2), flags);
            BinaryPrimitives.WriteUInt32LittleEndian(prefix.AsSpan(ExtentFormatConstants.HeaderLengthOffset, 4), (uint)headerJson.Length);

            await target.WriteAsync(prefix.AsMemory(), token).ConfigureAwait(false);
            await target.WriteAsync(headerJson.AsMemory(), token).ConfigureAwait(false);

            await CopyExactAsync(payload, target, header.SizeBytes, token).ConfigureAwait(false);

            await target.WriteAsync(hashBytes.AsMemory(), token).ConfigureAwait(false);

            byte[] closing = ExtentFormatConstants.ClosingMagic.ToArray();
            await target.WriteAsync(closing.AsMemory(), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task CopyExactAsync(Stream source, Stream destination, long count, CancellationToken token)
        {
            byte[] buffer = new byte[_CopyBufferBytes];
            long remaining = count;

            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = await source.ReadAsync(buffer.AsMemory(0, toRead), token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Payload is shorter than the declared size.");
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                remaining -= read;
            }
        }

        #endregion
    }
}
