namespace PepperX.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request capture and history configuration for the native REST plane.
    /// </summary>
    public class RequestHistorySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether request capture is enabled. Default true. When false, routes still register and return
        /// empty results.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Maximum request body bytes captured before truncation. Clamped to the range 0 to 1 MiB. Default
        /// 65536.
        /// </summary>
        public int MaxRequestBodyBytes
        {
            get
            {
                return _MaxRequestBodyBytes;
            }
            set
            {
                _MaxRequestBodyBytes = Math.Clamp(value, 0, 1048576);
            }
        }

        /// <summary>
        /// Maximum response body bytes captured before truncation. Clamped to the range 0 to 1 MiB. Default
        /// 65536.
        /// </summary>
        public int MaxResponseBodyBytes
        {
            get
            {
                return _MaxResponseBodyBytes;
            }
            set
            {
                _MaxResponseBodyBytes = Math.Clamp(value, 0, 1048576);
            }
        }

        /// <summary>
        /// Days before a captured entry is eligible for pruning. Clamped to the range 1 to 3650. Default 30.
        /// </summary>
        public int RetentionDays
        {
            get
            {
                return _RetentionDays;
            }
            set
            {
                _RetentionDays = Math.Clamp(value, 1, 3650);
            }
        }

        /// <summary>
        /// Glob-style path patterns whose request and response bodies are excluded from capture (used to keep
        /// object payloads off the data path). Never null.
        /// </summary>
        public List<string> ExcludeBodyPaths
        {
            get
            {
                return _ExcludeBodyPaths;
            }
            set
            {
                _ExcludeBodyPaths = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private int _MaxRequestBodyBytes = 65536;
        private int _MaxResponseBodyBytes = 65536;
        private int _RetentionDays = 30;
        private List<string> _ExcludeBodyPaths = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate request history settings.
        /// </summary>
        public RequestHistorySettings()
        {
        }

        #endregion
    }
}
