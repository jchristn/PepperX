namespace PepperX.Core.Telemetry
{
    /// <summary>
    /// A point-in-time view of extent storage capacity, published to the telemetry layer so the storage
    /// capacity gauges can observe it on collection without holding a reference to the driver.
    /// </summary>
    public readonly struct StorageCapacitySnapshot
    {
        #region Public-Members

        /// <summary>
        /// Total capacity of the storage volume, in bytes.
        /// </summary>
        public long TotalBytes { get; }

        /// <summary>
        /// Free capacity of the storage volume, in bytes.
        /// </summary>
        public long FreeBytes { get; }

        /// <summary>
        /// Used capacity of the storage volume, in bytes. Equal to total minus free.
        /// </summary>
        public long UsedBytes { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a capacity snapshot.
        /// </summary>
        /// <param name="totalBytes">Total capacity in bytes.</param>
        /// <param name="freeBytes">Free capacity in bytes.</param>
        public StorageCapacitySnapshot(long totalBytes, long freeBytes)
        {
            TotalBytes = totalBytes;
            FreeBytes = freeBytes;
            UsedBytes = totalBytes - freeBytes;
        }

        #endregion
    }
}
