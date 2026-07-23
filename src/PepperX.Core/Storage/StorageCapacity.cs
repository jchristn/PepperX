namespace PepperX.Core.Storage
{
    using System;

    /// <summary>
    /// Capacity of a storage volume as reported by a driver.
    /// </summary>
    public class StorageCapacity
    {
        #region Public-Members

        /// <summary>
        /// Total capacity in bytes. Zero when the driver cannot report it.
        /// </summary>
        public long TotalBytes
        {
            get
            {
                return _TotalBytes;
            }
        }

        /// <summary>
        /// Free capacity in bytes. Zero when the driver cannot report it.
        /// </summary>
        public long FreeBytes
        {
            get
            {
                return _FreeBytes;
            }
        }

        #endregion

        #region Private-Members

        private readonly long _TotalBytes;
        private readonly long _FreeBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a capacity report.
        /// </summary>
        /// <param name="totalBytes">Total capacity in bytes; zero or greater.</param>
        /// <param name="freeBytes">Free capacity in bytes; zero or greater.</param>
        /// <exception cref="ArgumentOutOfRangeException">A value is negative.</exception>
        public StorageCapacity(long totalBytes, long freeBytes)
        {
            if (totalBytes < 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
            if (freeBytes < 0) throw new ArgumentOutOfRangeException(nameof(freeBytes));
            _TotalBytes = totalBytes;
            _FreeBytes = freeBytes;
        }

        #endregion
    }
}
