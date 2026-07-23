namespace PepperX.Sdk.Models
{
    /// <summary>
    /// Sort ordering for an enumeration result set.
    /// </summary>
    public enum EnumerationOrderEnum
    {
        /// <summary>Oldest first, by creation time.</summary>
        CreatedAscending,

        /// <summary>Newest first, by creation time. This is the default.</summary>
        CreatedDescending,

        /// <summary>By key or name, ascending.</summary>
        KeyAscending,

        /// <summary>By key or name, descending.</summary>
        KeyDescending
    }
}
