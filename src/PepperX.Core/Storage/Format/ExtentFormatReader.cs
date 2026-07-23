namespace PepperX.Core.Storage.Format
{
    using System;
    using System.Buffers.Binary;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Serialization;
    using PepperX.Core.Storage;

    /// <summary>
    /// Reads and validates the PXE1 extent file format. This type is thread-safe; each call opens its own
    /// file handle.
    /// </summary>
    public static class ExtentFormatReader
    {
        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private static readonly int _CopyBufferBytes = 81920;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read only the header of an extent file.
        /// </summary>
        /// <param name="path">Extent file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The parsed header.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ExtentCorruptException">The file fails format validation.</exception>
        public static async Task<ExtentHeader> ReadHeaderOnlyAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous);
            try
            {
                return await ParseHeaderAsync(fs, path, token).ConfigureAwait(false);
            }
            finally
            {
                await fs.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Open the full payload of an extent file for streaming.
        /// </summary>
        /// <param name="path">Extent file path.</param>
        /// <param name="verifyChecksum">When true, the payload checksum is verified before returning.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream positioned at the payload start. The caller must dispose it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ExtentCorruptException">The file fails format or checksum validation.</exception>
        public static async Task<ExtentPayloadStream> OpenPayloadAsync(string path, bool verifyChecksum, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous);
            try
            {
                ExtentHeader header = await ParseHeaderAsync(fs, path, token).ConfigureAwait(false);
                long payloadStart = fs.Position;
                ValidateTrailer(fs, path, payloadStart, header.SizeBytes);

                if (verifyChecksum)
                {
                    await VerifyChecksumAsync(fs, path, payloadStart, header, token).ConfigureAwait(false);
                }

                fs.Seek(payloadStart, SeekOrigin.Begin);
                ExtentPayloadStream payload = new ExtentPayloadStream(fs, header, header.SizeBytes);
                fs = null!;
                return payload;
            }
            finally
            {
                if (fs != null) await fs.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Open a byte range of an extent file's payload for streaming.
        /// </summary>
        /// <param name="path">Extent file path.</param>
        /// <param name="offset">Zero-based payload offset; must be within the payload.</param>
        /// <param name="count">Number of bytes to expose; clamped to the remaining payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A payload stream windowed to the requested range. The caller must dispose it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null or empty.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> or <paramref name="count"/> is out of range.</exception>
        /// <exception cref="ExtentCorruptException">The file fails format validation.</exception>
        public static async Task<ExtentPayloadStream> OpenRangeAsync(string path, long offset, long count, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, _CopyBufferBytes, FileOptions.Asynchronous);
            try
            {
                ExtentHeader header = await ParseHeaderAsync(fs, path, token).ConfigureAwait(false);
                long payloadStart = fs.Position;
                ValidateTrailer(fs, path, payloadStart, header.SizeBytes);

                if (offset > header.SizeBytes) throw new ArgumentOutOfRangeException(nameof(offset), "Offset is beyond the end of the payload.");
                long window = Math.Min(count, header.SizeBytes - offset);

                fs.Seek(payloadStart + offset, SeekOrigin.Begin);
                ExtentPayloadStream payload = new ExtentPayloadStream(fs, header, window);
                fs = null!;
                return payload;
            }
            finally
            {
                if (fs != null) await fs.DisposeAsync().ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<ExtentHeader> ParseHeaderAsync(FileStream fs, string path, CancellationToken token)
        {
            byte[] prefix = new byte[ExtentFormatConstants.PrefixLength];
            await ReadExactAsync(fs, prefix, path, token).ConfigureAwait(false);

            if (!prefix.AsSpan(0, 4).SequenceEqual(ExtentFormatConstants.Magic))
                throw new ExtentCorruptException("Extent file '" + path + "' has an invalid leading magic.");

            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(4, 2));
            if (version == 0 || version > ExtentFormatConstants.FormatVersion)
                throw new ExtentCorruptException("Extent file '" + path + "' has an unsupported format version (" + version + ").");

            uint headerLength = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(ExtentFormatConstants.HeaderLengthOffset, 4));
            if (headerLength == 0 || headerLength > 16 * 1024 * 1024)
                throw new ExtentCorruptException("Extent file '" + path + "' has an invalid header length (" + headerLength + ").");

            byte[] headerJson = new byte[headerLength];
            await ReadExactAsync(fs, headerJson, path, token).ConfigureAwait(false);

            try
            {
                ExtentHeader header = _Serializer.DeserializeJson<ExtentHeader>(Encoding.UTF8.GetString(headerJson));
                if (header == null) throw new ExtentCorruptException("Extent file '" + path + "' has a null header.");
                return header;
            }
            catch (ExtentCorruptException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ExtentCorruptException("Extent file '" + path + "' has an unparseable header.", ex);
            }
        }

        private static void ValidateTrailer(FileStream fs, string path, long payloadStart, long sizeBytes)
        {
            long expected = payloadStart + sizeBytes + ExtentFormatConstants.TrailerLength;
            if (fs.Length != expected)
                throw new ExtentCorruptException("Extent file '" + path + "' has an unexpected length (" + fs.Length + ", expected " + expected + ").");

            byte[] closing = new byte[ExtentFormatConstants.ClosingLength];
            fs.Seek(fs.Length - ExtentFormatConstants.ClosingLength, SeekOrigin.Begin);
            int read = fs.Read(closing, 0, closing.Length);
            if (read != closing.Length || !closing.AsSpan().SequenceEqual(ExtentFormatConstants.ClosingMagic))
                throw new ExtentCorruptException("Extent file '" + path + "' is truncated or has an invalid closing magic.");
        }

        private static async Task VerifyChecksumAsync(FileStream fs, string path, long payloadStart, ExtentHeader header, CancellationToken token)
        {
            fs.Seek(payloadStart, SeekOrigin.Begin);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[_CopyBufferBytes];
                long remaining = header.SizeBytes;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(buffer.Length, remaining);
                    int read = await fs.ReadAsync(buffer.AsMemory(0, toRead), token).ConfigureAwait(false);
                    if (read == 0) throw new ExtentCorruptException("Extent file '" + path + "' payload ended early during checksum verification.");
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string actual = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (!String.Equals(actual, header.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new ExtentCorruptException("Extent file '" + path + "' failed checksum verification.");
            }
        }

        private static async Task ReadExactAsync(FileStream fs, byte[] buffer, string path, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await fs.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token).ConfigureAwait(false);
                if (read == 0) throw new ExtentCorruptException("Extent file '" + path + "' is truncated.");
                offset += read;
            }
        }

        #endregion
    }
}
