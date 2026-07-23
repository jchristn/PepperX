namespace Test.Shared
{
    using System;
    using PepperX.Core.Enums;
    using PepperX.Core.Settings;

    /// <summary>
    /// Resolves connection details for the dockerized test PostgreSQL instance. Values come from
    /// <c>PEPPERX_TEST_DB_*</c> environment variables, defaulting to the settings in
    /// <c>docker/compose.test.yaml</c> (host localhost, port 5433).
    /// </summary>
    public static class TestEnvironment
    {
        /// <summary>
        /// Test database host.
        /// </summary>
        public static string Host => Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_HOST") ?? "localhost";

        /// <summary>
        /// Test database port.
        /// </summary>
        public static int Port => int.TryParse(Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_PORT"), out int p) ? p : 5433;

        /// <summary>
        /// Test database user.
        /// </summary>
        public static string User => Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_USER") ?? "pepperx_test";

        /// <summary>
        /// Test database password.
        /// </summary>
        public static string Password => Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_PASSWORD") ?? "pepperx_test";

        /// <summary>
        /// Base (maintenance) database used to create and drop per-run databases.
        /// </summary>
        public static string BaseDatabase => Environment.GetEnvironmentVariable("PEPPERX_TEST_DB_NAME") ?? "pepperx_test";

        /// <summary>
        /// Build database settings targeting a specific database name.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns>Database settings.</returns>
        public static DatabaseSettings SettingsFor(string databaseName)
        {
            return new DatabaseSettings
            {
                Type = DatabaseTypeEnum.Postgresql,
                Hostname = Host,
                Port = Port,
                DatabaseName = databaseName,
                Username = User,
                Password = Password,

                // Several drivers (the shared suite driver, the in-process server, and per-test isolated
                // databases) run concurrently against one test instance; keep each pool small so the tests
                // stay well inside the server's connection limit.
                MaxPoolSize = 15
            };
        }

        /// <summary>
        /// Build an admin connection string targeting the base database (short timeout).
        /// </summary>
        /// <returns>Connection string.</returns>
        public static string AdminConnectionString()
        {
            return "Host=" + Host + ";Port=" + Port + ";Database=" + BaseDatabase + ";Username=" + User + ";Password=" + Password + ";Timeout=2;Command Timeout=10;";
        }
    }
}
