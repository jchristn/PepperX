namespace PepperX.Core.Enums
{
    /// <summary>
    /// Lifecycle state of an extent.
    /// </summary>
    public enum ExtentStateEnum
    {
        /// <summary>
        /// The extent is live and readable. Exactly one active extent may exist per (container, key).
        /// </summary>
        Active,

        /// <summary>
        /// The extent is tombstoned and pending physical destruction. No new reads are admitted; existing
        /// in-flight reads drain before the payload is deleted.
        /// </summary>
        Deleting
    }
}
