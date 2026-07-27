namespace PepperX.Core.Helpers
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Streaming hash utilities (SHA-256 and MD5). All members are thread-safe; each call uses its own
    /// hash instances.
    /// </summary>
    public static class HashHelper
    {
        #region Private-Members

        private static readonly int _CopyBufferBytes = 81920;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy <paramref name="source"/> to <paramref name="destination"/> while computing the SHA-256 of the bytes copied.
        /// The streams are not disposed by this method.
        /// </summary>
        /// <param name="source">Source stream to read to end.</param>
        /// <param name="destination">Destination stream to write the copied bytes to.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lowercase hex SHA-256 and MD5 of the copied bytes, and the total byte count.</returns>
        /// <exception cref="ArgumentNullException">A stream argument is null.</exception>
        public static async Task<HashResult> CopyAndHashAsync(Stream source, Stream destination, CancellationToken token = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            using (SHA256 sha = SHA256.Create())
            using (MD5 md5 = MD5.Create())
            {
                byte[] buffer = new byte[_CopyBufferBytes];
                long total = 0;

                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    md5.TransformBlock(buffer, 0, read, null, 0);
                    total += read;
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return new HashResult(ToHex(sha.Hash!), ToHex(md5.Hash!), total);
            }
        }

        /// <summary>
        /// Compute the lowercase hex SHA-256 of a byte array.
        /// </summary>
        /// <param name="data">Data to hash.</param>
        /// <returns>Lowercase hex SHA-256.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        public static string HashBytes(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return ToHex(SHA256.HashData(data));
        }

        #endregion

        #region Private-Methods

        private static string ToHex(byte[] hash)
        {
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        #endregion
    }
}
