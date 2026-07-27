namespace PepperX.Core.Helpers
{
    using System;

    /// <summary>
    /// Result of a streaming hash operation: the computed hash and the number of bytes processed.
    /// </summary>
    public sealed class HashResult
    {
        #region Public-Members

        /// <summary>
        /// Lowercase hex SHA-256 of the processed bytes.
        /// </summary>
        public string Sha256
        {
            get
            {
                return _Sha256;
            }
        }

        /// <summary>
        /// Lowercase hex MD5 of the processed bytes.
        /// </summary>
        public string Md5
        {
            get
            {
                return _Md5;
            }
        }

        /// <summary>
        /// Total number of bytes processed.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _Sha256;
        private readonly string _Md5;
        private readonly long _SizeBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a hash result.
        /// </summary>
        /// <param name="sha256">Lowercase hex SHA-256.</param>
        /// <param name="md5">Lowercase hex MD5.</param>
        /// <param name="sizeBytes">Total bytes processed; must be zero or greater.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sha256"/> or <paramref name="md5"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sizeBytes"/> is negative.</exception>
        public HashResult(string sha256, string md5, long sizeBytes)
        {
            if (sha256 == null) throw new ArgumentNullException(nameof(sha256));
            if (md5 == null) throw new ArgumentNullException(nameof(md5));
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

            _Sha256 = sha256;
            _Md5 = md5;
            _SizeBytes = sizeBytes;
        }

        #endregion
    }
}
