namespace PepperX.Server
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Responses;
    using PepperX.Core.Serialization;
    using PepperX.Core.Services;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage.Disk;
    using PepperX.Server.Services;
    using SyslogLogging;

    /// <summary>
    /// Composition root and command-line dispatcher. Supports the default <c>serve</c> action and a
    /// <c>rehydrate --mode Verify|Repair|Rebuild</c> maintenance action.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>
        /// Run the requested action.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            PepperXSettings settings = SettingsManager.Load();
            LoggingModule logging = CreateLogging(settings.Logging);

            if (args.Length > 0 && String.Equals(args[0], "rehydrate", StringComparison.OrdinalIgnoreCase))
            {
                return await RehydrateAsync(args, settings, logging).ConfigureAwait(false);
            }

            Console.WriteLine(Constants.Logo);
            Console.WriteLine(Constants.ProductName + " v" + Constants.ProductVersion);

            string serviceInstanceId = String.IsNullOrEmpty(settings.Cluster.NodeId)
                ? Environment.MachineName
                : settings.Cluster.NodeId;
            TelemetryModule telemetry = TelemetryModule.Start(settings.Telemetry, serviceInstanceId, logging);

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                PepperXServer server = new PepperXServer(settings, logging);
                try
                {
                    await server.RunAsync(cts.Token).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex)
                {
                    logging.Error("[Bootstrapper] fatal error: " + ex.ToString());
                    return 1;
                }
                finally
                {
                    telemetry.ForceFlush();
                    telemetry.Dispose();
                }
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<int> RehydrateAsync(string[] args, PepperXSettings settings, LoggingModule logging)
        {
            RehydrationModeEnum mode = RehydrationModeEnum.Verify;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (String.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Enum.TryParse(args[i + 1], ignoreCase: true, out mode)) mode = RehydrationModeEnum.Verify;
                }
            }

            IMetadataDatabaseDriver db = await MetadataDatabaseDriverFactory.CreateAndInitializeAsync(settings.Database, logging).ConfigureAwait(false);
            try
            {
                DiskExtentStorageDriver storage = new DiskExtentStorageDriver(settings.Storage.Disk);
                await storage.InitializeAsync().ConfigureAwait(false);

                RehydrationService rehydration = new RehydrationService(db, storage, logging);
                RehydrationReport report = await rehydration.RehydrateAsync(mode).ConfigureAwait(false);

                PepperXSerializer serializer = new PepperXSerializer();
                Console.WriteLine(serializer.SerializeJson(report, true));
                return report.Success ? 0 : 1;
            }
            finally
            {
                await db.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static LoggingModule CreateLogging(PepperX.Core.Settings.LoggingSettings settings)
        {
            if (settings.FileLogging)
            {
                if (!Directory.Exists(settings.LogDirectory)) Directory.CreateDirectory(settings.LogDirectory);
                string path = Path.Combine(settings.LogDirectory, settings.LogFilename);
                return new LoggingModule(path, FileLoggingMode.SingleLogFile, settings.ConsoleLogging);
            }

            return new LoggingModule();
        }

        #endregion
    }
}
