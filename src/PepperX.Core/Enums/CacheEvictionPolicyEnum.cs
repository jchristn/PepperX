namespace PepperX.Core.Enums
{
    /// <summary>
    /// The eviction policy a container cache uses when it reaches capacity or its memory limit.
    /// </summary>
    public enum CacheEvictionPolicyEnum
    {
        /// <summary>
        /// First-in, first-out: evicts the oldest-inserted entries. Cheapest to maintain; ignores how
        /// recently an entry was read.
        /// </summary>
        FIFO,

        /// <summary>
        /// Least-recently-used: evicts the entries that have gone longest without a read. Keeps hot
        /// objects resident at the cost of tracking access order. This is the default.
        /// </summary>
        LRU
    }
}
