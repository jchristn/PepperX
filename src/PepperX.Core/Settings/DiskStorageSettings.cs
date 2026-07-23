namespace PepperX.Core.Settings
{
    using System;

    /// <summary>
    /// Configuration for the disk extent storage driver.
    /// </summary>
    public class DiskStorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Root directory under which extent files are stored. For multi-node deployments this must point at
        /// a shared filesystem accessible to every node. Default "./data/extents".
        /// </summary>
        public string RootDirectory
        {
            get
            {
                return _RootDirectory;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RootDirectory));
                _RootDirectory = value;
            }
        }

        #endregion

        #region Private-Members

        private string _RootDirectory = "./data/extents";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate disk storage settings.
        /// </summary>
        public DiskStorageSettings()
        {
        }

        #endregion
    }
}
