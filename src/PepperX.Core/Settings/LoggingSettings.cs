namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Logging configuration.
    /// </summary>
    public class LoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to log to the console. Default true.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether to log to a file. Default true.
        /// </summary>
        public bool FileLogging { get; set; } = true;

        /// <summary>
        /// Directory for log files. Default "logs".
        /// </summary>
        public string LogDirectory
        {
            get
            {
                return _LogDirectory;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LogDirectory));
                _LogDirectory = value;
            }
        }

        /// <summary>
        /// Log file name. Default "pepperx.log".
        /// </summary>
        public string LogFilename
        {
            get
            {
                return _LogFilename;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LogFilename));
                _LogFilename = value;
            }
        }

        /// <summary>
        /// Minimum severity to emit (Debug, Info, Warn, Error, Alert, Critical, Emergency). Default "Info".
        /// </summary>
        public string MinimumSeverity
        {
            get
            {
                return _MinimumSeverity;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(MinimumSeverity));
                _MinimumSeverity = value;
            }
        }

        #endregion

        #region Private-Members

        private string _LogDirectory = "logs";
        private string _LogFilename = "pepperx.log";
        private string _MinimumSeverity = "Info";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate logging settings.
        /// </summary>
        public LoggingSettings()
        {
        }

        #endregion
    }
}
