namespace PepperX.Core.Storage
{
    using System;

    /// <summary>
    /// Result of persisting an extent to a storage driver.
    /// </summary>
    public class ExtentWriteResult
    {
        #region Public-Members

        /// <summary>
        /// Payload size in bytes.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the payload.
        /// </summary>
        public string Sha256
        {
            get
            {
                return _Sha256;
            }
        }

        /// <summary>
        /// Lowercase hex MD5 of the payload (the content hash used to derive the S3 ETag).
        /// </summary>
        public string Md5
        {
            get
            {
                return _Md5;
            }
        }

        /// <summary>
        /// Driver-relative location of the stored extent.
        /// </summary>
        public string Location
        {
            get
            {
                return _Location;
            }
        }

        #endregion

        #region Private-Members

        private readonly long _SizeBytes;
        private readonly string _Sha256;
        private readonly string _Md5;
        private readonly string _Location;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an extent write result.
        /// </summary>
        /// <param name="sizeBytes">Payload size in bytes; zero or greater.</param>
        /// <param name="sha256">Lowercase hex SHA-256.</param>
        /// <param name="md5">Lowercase hex MD5.</param>
        /// <param name="location">Driver-relative location.</param>
        /// <exception cref="ArgumentNullException"><paramref name="sha256"/>, <paramref name="md5"/>, or <paramref name="location"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sizeBytes"/> is negative.</exception>
        public ExtentWriteResult(long sizeBytes, string sha256, string md5, string location)
        {
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            _SizeBytes = sizeBytes;
            _Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
            _Md5 = md5 ?? throw new ArgumentNullException(nameof(md5));
            _Location = location ?? throw new ArgumentNullException(nameof(location));
        }

        #endregion
    }
}
