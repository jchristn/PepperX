namespace PepperX.Core.Storage
{
    using System;

    /// <summary>
    /// Result of staging a multipart part payload to a storage driver. Carries the staged size, both
    /// content hashes computed in the same streamed pass, and the driver-relative location.
    /// </summary>
    public class MultipartStageResult
    {
        #region Public-Members

        /// <summary>
        /// Staged payload size in bytes; zero or greater.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _SizeBytes;
            }
        }

        /// <summary>
        /// Lowercase hex MD5 of the staged part.
        /// </summary>
        public string Md5
        {
            get
            {
                return _Md5;
            }
        }

        /// <summary>
        /// Lowercase hex SHA-256 of the staged part.
        /// </summary>
        public string Sha256
        {
            get
            {
                return _Sha256;
            }
        }

        /// <summary>
        /// Driver-relative location of the staged part.
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
        private readonly string _Md5;
        private readonly string _Sha256;
        private readonly string _Location;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a multipart stage result.
        /// </summary>
        /// <param name="sizeBytes">Staged size in bytes; zero or greater.</param>
        /// <param name="md5">Lowercase hex MD5.</param>
        /// <param name="sha256">Lowercase hex SHA-256.</param>
        /// <param name="location">Driver-relative location.</param>
        /// <exception cref="ArgumentNullException"><paramref name="md5"/>, <paramref name="sha256"/>, or <paramref name="location"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="sizeBytes"/> is negative.</exception>
        public MultipartStageResult(long sizeBytes, string md5, string sha256, string location)
        {
            if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            _SizeBytes = sizeBytes;
            _Md5 = md5 ?? throw new ArgumentNullException(nameof(md5));
            _Sha256 = sha256 ?? throw new ArgumentNullException(nameof(sha256));
            _Location = location ?? throw new ArgumentNullException(nameof(location));
        }

        #endregion
    }
}
