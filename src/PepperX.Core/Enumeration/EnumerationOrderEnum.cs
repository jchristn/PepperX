namespace PepperX.Core.Enumeration
{
    /// <summary>
    /// Sort ordering for an enumeration result set.
    /// </summary>
    public enum EnumerationOrderEnum
    {
        /// <summary>
        /// Oldest first, by creation time (ascending identifier).
        /// </summary>
        CreatedAscending,

        /// <summary>
        /// Newest first, by creation time (descending identifier). This is the default.
        /// </summary>
        CreatedDescending,

        /// <summary>
        /// By key or name, ascending (ordinal).
        /// </summary>
        KeyAscending,

        /// <summary>
        /// By key or name, descending (ordinal).
        /// </summary>
        KeyDescending
    }
}
