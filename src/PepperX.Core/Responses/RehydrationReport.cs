namespace PepperX.Core.Responses
{
    using System.Collections.Generic;
    using PepperX.Core.Enums;

    /// <summary>
    /// Outcome of a rehydration run.
    /// </summary>
    public class RehydrationReport
    {
        #region Public-Members

        /// <summary>
        /// Mode the run executed in.
        /// </summary>
        public RehydrationModeEnum Mode { get; set; } = RehydrationModeEnum.Verify;

        /// <summary>
        /// Whether the run completed without error.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Number of containers discovered in storage.
        /// </summary>
        public long ContainersDiscovered { get; set; } = 0;

        /// <summary>
        /// Number of extents discovered in storage.
        /// </summary>
        public long ExtentsDiscovered { get; set; } = 0;

        /// <summary>
        /// Number of database rows added.
        /// </summary>
        public long RowsAdded { get; set; } = 0;

        /// <summary>
        /// Number of database rows removed.
        /// </summary>
        public long RowsRemoved { get; set; } = 0;

        /// <summary>
        /// Human-readable descriptions of drift found (for verify mode) or reconciled. Never null.
        /// </summary>
        public List<string> Drift
        {
            get
            {
                return _Drift;
            }
            set
            {
                _Drift = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Total run duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<string> _Drift = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty rehydration report.
        /// </summary>
        public RehydrationReport()
        {
        }

        #endregion
    }
}
