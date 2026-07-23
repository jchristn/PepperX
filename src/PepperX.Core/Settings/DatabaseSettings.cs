namespace PepperX.Core.Settings
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Metadata database configuration.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Database provider. Default <see cref="DatabaseTypeEnum.Postgresql"/>.
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Postgresql;

        /// <summary>
        /// Database host name. Default "localhost".
        /// </summary>
        public string Hostname
        {
            get
            {
                return _Hostname;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Hostname));
                _Hostname = value;
            }
        }

        /// <summary>
        /// Database port. Clamped to the range 1 to 65535. Default 5432.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Database name. Default "pepperx".
        /// </summary>
        public string DatabaseName
        {
            get
            {
                return _DatabaseName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DatabaseName));
                _DatabaseName = value;
            }
        }

        /// <summary>
        /// Database user name. Default "pepperx".
        /// </summary>
        public string Username
        {
            get
            {
                return _Username;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Username));
                _Username = value;
            }
        }

        /// <summary>
        /// Database password. May be empty. Default "pepperx".
        /// </summary>
        public string Password
        {
            get
            {
                return _Password;
            }
            set
            {
                _Password = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Maximum connection pool size. Clamped to the range 1 to 1000. Default 100.
        /// </summary>
        public int MaxPoolSize
        {
            get
            {
                return _MaxPoolSize;
            }
            set
            {
                _MaxPoolSize = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "localhost";
        private int _Port = 5432;
        private string _DatabaseName = "pepperx";
        private string _Username = "pepperx";
        private string _Password = "pepperx";
        private int _MaxPoolSize = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate database settings.
        /// </summary>
        public DatabaseSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a PostgreSQL connection string from these settings.
        /// </summary>
        /// <returns>Connection string.</returns>
        public string BuildConnectionString()
        {
            return
                "Host=" + _Hostname + ";" +
                "Port=" + _Port + ";" +
                "Database=" + _DatabaseName + ";" +
                "Username=" + _Username + ";" +
                "Password=" + _Password + ";" +
                "Maximum Pool Size=" + _MaxPoolSize + ";";
        }

        #endregion
    }
}
