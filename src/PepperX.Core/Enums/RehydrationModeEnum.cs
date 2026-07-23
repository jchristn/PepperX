namespace PepperX.Core.Enums
{
    /// <summary>
    /// Mode governing how a rehydration run reconciles the metadata database with raw extent storage.
    /// </summary>
    public enum RehydrationModeEnum
    {
        /// <summary>
        /// Report drift between storage and the database; change nothing.
        /// </summary>
        Verify,

        /// <summary>
        /// Add database rows for extents present in storage but missing from the database, and remove
        /// database rows whose backing file is gone. Existing consistent rows are left untouched.
        /// </summary>
        Repair,

        /// <summary>
        /// Truncate the metadata tables and rebuild them entirely from raw extent storage.
        /// </summary>
        Rebuild
    }
}
