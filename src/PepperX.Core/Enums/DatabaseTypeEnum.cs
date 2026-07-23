namespace PepperX.Core.Enums
{
    /// <summary>
    /// Supported metadata database providers. PepperX ships with PostgreSQL; the enum and factory
    /// leave room for additional providers behind the same interface.
    /// </summary>
    public enum DatabaseTypeEnum
    {
        /// <summary>
        /// PostgreSQL. The shared, authoritative metadata store.
        /// </summary>
        Postgresql
    }
}
