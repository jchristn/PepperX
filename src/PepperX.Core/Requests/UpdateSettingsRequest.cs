namespace PepperX.Core.Requests
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// A partial update to a node's settings.
    /// <para>
    /// Every field is nullable: only the ones supplied are changed, so a caller can update one
    /// setting without restating the rest. The editable surface is deliberately narrow — the fields
    /// here are operational knobs that are safe to change from an unauthenticated console. Database
    /// connection details, protocol ports, and hostnames are intentionally excluded: getting one of
    /// those wrong from a web form would leave the node unable to start after the restart that applies
    /// it, with no way back in.
    /// </para>
    /// <para>
    /// Changes take effect after a restart. The server captures settings into its services at startup,
    /// so persisting them updates the file the next boot reads rather than the running process.
    /// </para>
    /// </summary>
    public class UpdateSettingsRequest
    {
        #region Public-Members

        /// <summary>
        /// Minimum log severity to emit: Debug, Info, Warn, Error, Alert, Critical, or Emergency.
        /// </summary>
        public string? LogMinimumSeverity { get; set; } = null;

        /// <summary>
        /// Whether to verify each extent's checksum on read.
        /// </summary>
        public bool? VerifyChecksumOnRead { get; set; } = null;

        /// <summary>
        /// How deletes coordinate with in-flight reads across nodes.
        /// </summary>
        public DeleteCoordinationModeEnum? DeleteCoordinationMode { get; set; } = null;

        /// <summary>
        /// Whether request history is captured.
        /// </summary>
        public bool? RequestHistoryEnabled { get; set; } = null;

        /// <summary>
        /// Days of request history to retain.
        /// </summary>
        public int? RequestHistoryRetentionDays { get; set; } = null;

        /// <summary>
        /// Maximum captured request-body size, in bytes.
        /// </summary>
        public int? RequestHistoryMaxRequestBodyBytes { get; set; } = null;

        /// <summary>
        /// Maximum captured response-body size, in bytes.
        /// </summary>
        public int? RequestHistoryMaxResponseBodyBytes { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty update request.
        /// </summary>
        public UpdateSettingsRequest()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply the supplied fields onto a settings instance.
        /// </summary>
        /// <param name="settings">Settings to mutate.</param>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is null.</exception>
        public void ApplyTo(Settings.PepperXSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (!String.IsNullOrWhiteSpace(LogMinimumSeverity)) settings.Logging.MinimumSeverity = LogMinimumSeverity;
            if (VerifyChecksumOnRead.HasValue) settings.Storage.VerifyChecksumOnRead = VerifyChecksumOnRead.Value;
            if (DeleteCoordinationMode.HasValue) settings.Cluster.DeleteCoordinationMode = DeleteCoordinationMode.Value;

            if (RequestHistoryEnabled.HasValue) settings.RequestHistory.Enabled = RequestHistoryEnabled.Value;
            if (RequestHistoryRetentionDays.HasValue) settings.RequestHistory.RetentionDays = RequestHistoryRetentionDays.Value;
            if (RequestHistoryMaxRequestBodyBytes.HasValue) settings.RequestHistory.MaxRequestBodyBytes = RequestHistoryMaxRequestBodyBytes.Value;
            if (RequestHistoryMaxResponseBodyBytes.HasValue) settings.RequestHistory.MaxResponseBodyBytes = RequestHistoryMaxResponseBodyBytes.Value;
        }

        #endregion
    }
}
