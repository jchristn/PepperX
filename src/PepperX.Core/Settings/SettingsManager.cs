namespace PepperX.Core.Settings
{
    using System;
    using System.Globalization;
    using System.IO;
    using PepperX.Core.Serialization;

    /// <summary>
    /// Loads, persists, and environment-overrides <see cref="PepperXSettings"/>. Loading order is: JSON file
    /// (created with defaults if missing), then environment variable overrides.
    /// </summary>
    public static class SettingsManager
    {
        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();
        private static readonly string _DefaultSettingsFile = "pepperx.json";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load settings from a file (creating a default file if it does not exist), then apply environment
        /// overrides.
        /// </summary>
        /// <param name="path">
        /// Settings file path. When null, the <c>PEPPERX_SETTINGS_FILE</c> environment variable is used, then
        /// "pepperx.json".
        /// </param>
        /// <returns>Loaded and overridden settings.</returns>
        public static PepperXSettings Load(string? path = null)
        {
            string resolved = ResolvePath(path);

            PepperXSettings settings;
            if (File.Exists(resolved))
            {
                string json = File.ReadAllText(resolved);
                settings = _Serializer.DeserializeJson<PepperXSettings>(json) ?? new PepperXSettings();
            }
            else
            {
                settings = new PepperXSettings();
                Save(settings, resolved);
            }

            ApplyEnvironmentOverrides(settings);
            return settings;
        }

        /// <summary>
        /// Persist settings to a file.
        /// </summary>
        /// <param name="settings">Settings to persist.</param>
        /// <param name="path">Destination file path.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null or <paramref name="path"/> is empty.</exception>
        public static void Save(PepperXSettings settings, string path)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            string json = _Serializer.SerializeJson(settings, true) ?? "{}";
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Apply environment variable overrides to a settings instance in place.
        /// </summary>
        /// <param name="settings">Settings to override.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public static void ApplyEnvironmentOverrides(PepperXSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string? dbHost = Environment.GetEnvironmentVariable("PEPPERX_DB_HOSTNAME");
            if (!String.IsNullOrEmpty(dbHost)) settings.Database.Hostname = dbHost;

            settings.Database.Port = OverridePort("PEPPERX_DB_PORT", settings.Database.Port);

            string? dbName = Environment.GetEnvironmentVariable("PEPPERX_DB_DATABASE");
            if (!String.IsNullOrEmpty(dbName)) settings.Database.DatabaseName = dbName;

            string? dbUser = Environment.GetEnvironmentVariable("PEPPERX_DB_USERNAME");
            if (!String.IsNullOrEmpty(dbUser)) settings.Database.Username = dbUser;

            string? dbPass = Environment.GetEnvironmentVariable("PEPPERX_DB_PASSWORD");
            if (dbPass != null) settings.Database.Password = dbPass;

            string? storageRoot = Environment.GetEnvironmentVariable("PEPPERX_STORAGE_ROOT");
            if (!String.IsNullOrEmpty(storageRoot)) settings.Storage.Disk.RootDirectory = storageRoot;

            string? nodeId = Environment.GetEnvironmentVariable("PEPPERX_NODE_ID");
            if (!String.IsNullOrEmpty(nodeId)) settings.Cluster.NodeId = nodeId;

            settings.Rest.Port = OverridePort("PEPPERX_REST_PORT", settings.Rest.Port);
            settings.S3.Port = OverridePort("PEPPERX_S3_PORT", settings.S3.Port);
            settings.Resp.Port = OverridePort("PEPPERX_RESP_PORT", settings.Resp.Port);
            settings.Websocket.Port = OverridePort("PEPPERX_WS_PORT", settings.Websocket.Port);
            settings.Mcp.HttpPort = OverridePort("PEPPERX_MCP_HTTP_PORT", settings.Mcp.HttpPort);
            settings.Mcp.TcpPort = OverridePort("PEPPERX_MCP_TCP_PORT", settings.Mcp.TcpPort);
        }

        #endregion

        /// <summary>
        /// Resolve the settings file path the same way <see cref="Load"/> does.
        /// <para>
        /// Exposed so the admin surface can persist edits to exactly the file the next startup will
        /// read, rather than guessing at the path.
        /// </para>
        /// </summary>
        /// <param name="path">Explicit path, or null to use the environment variable then the default.</param>
        /// <returns>Resolved path.</returns>
        public static string ResolveSettingsPath(string? path = null)
        {
            return ResolvePath(path);
        }

        #region Private-Methods

        private static string ResolvePath(string? path)
        {
            if (!String.IsNullOrWhiteSpace(path)) return path;

            string? fromEnv = Environment.GetEnvironmentVariable("PEPPERX_SETTINGS_FILE");
            if (!String.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

            return _DefaultSettingsFile;
        }

        private static int OverridePort(string variable, int current)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            if (String.IsNullOrEmpty(value)) return current;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            return current;
        }

        #endregion
    }
}
