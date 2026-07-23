namespace PepperX.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database.Postgresql;
    using PepperX.Core.Enums;
    using PepperX.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Composition root for the metadata database layer.
    /// </summary>
    public static class MetadataDatabaseDriverFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a driver for the configured provider.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <returns>A database driver.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        /// <exception cref="ArgumentException">The provider type is not supported.</exception>
        public static IMetadataDatabaseDriver Create(DatabaseSettings settings, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlMetadataDatabaseDriver(settings, logging);
                default:
                    throw new ArgumentException("Unsupported database type: " + settings.Type);
            }
        }

        /// <summary>
        /// Create and initialize a driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An initialized database driver.</returns>
        public static async Task<IMetadataDatabaseDriver> CreateAndInitializeAsync(DatabaseSettings settings, LoggingModule? logging = null, CancellationToken token = default)
        {
            IMetadataDatabaseDriver driver = Create(settings, logging);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }

        #endregion
    }
}
