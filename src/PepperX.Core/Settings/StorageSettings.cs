namespace PepperX.Core.Settings
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Extent storage configuration and object/metadata size limits.
    /// </summary>
    public class StorageSettings
    {
        #region Public-Members

        /// <summary>
        /// Storage driver to use. Default <see cref="StorageDriverTypeEnum.Disk"/>.
        /// </summary>
        public StorageDriverTypeEnum Driver { get; set; } = StorageDriverTypeEnum.Disk;

        /// <summary>
        /// Disk driver settings. Never null.
        /// </summary>
        public DiskStorageSettings Disk
        {
            get
            {
                return _Disk;
            }
            set
            {
                _Disk = value ?? new DiskStorageSettings();
            }
        }

        /// <summary>
        /// Whether to verify the payload checksum on every read. Default false (checksums are always verified
        /// during rehydration regardless of this setting).
        /// </summary>
        public bool VerifyChecksumOnRead { get; set; } = false;

        /// <summary>
        /// Maximum object payload size in bytes. Clamped to the range 1 to 1 TiB. Default 5 GiB.
        /// </summary>
        public long MaxObjectBytes
        {
            get
            {
                return _MaxObjectBytes;
            }
            set
            {
                _MaxObjectBytes = Math.Clamp(value, 1L, 1099511627776L);
            }
        }

        /// <summary>
        /// Maximum object key length in bytes. Clamped to the range 1 to 4096. Default 1024.
        /// </summary>
        public int MaxKeyBytes
        {
            get
            {
                return _MaxKeyBytes;
            }
            set
            {
                _MaxKeyBytes = Math.Clamp(value, 1, 4096);
            }
        }

        /// <summary>
        /// Maximum number of labels per object. Clamped to the range 0 to 1024. Default 64.
        /// </summary>
        public int MaxLabels
        {
            get
            {
                return _MaxLabels;
            }
            set
            {
                _MaxLabels = Math.Clamp(value, 0, 1024);
            }
        }

        /// <summary>
        /// Maximum length of a single label. Clamped to the range 1 to 4096. Default 512.
        /// </summary>
        public int MaxLabelLength
        {
            get
            {
                return _MaxLabelLength;
            }
            set
            {
                _MaxLabelLength = Math.Clamp(value, 1, 4096);
            }
        }

        /// <summary>
        /// Maximum number of tags per object. Clamped to the range 0 to 1024. Default 64.
        /// </summary>
        public int MaxTags
        {
            get
            {
                return _MaxTags;
            }
            set
            {
                _MaxTags = Math.Clamp(value, 0, 1024);
            }
        }

        /// <summary>
        /// Maximum serialized size of the freeform metadata object in bytes. Clamped to the range 0 to
        /// 64 MiB. Default 1 MiB.
        /// </summary>
        public int MaxMetadataObjectBytes
        {
            get
            {
                return _MaxMetadataObjectBytes;
            }
            set
            {
                _MaxMetadataObjectBytes = Math.Clamp(value, 0, 67108864);
            }
        }

        /// <summary>
        /// Number of times to retry a replace that loses a race before failing. Clamped to the range 0 to 20.
        /// Default 3.
        /// </summary>
        public int ReplaceRetryCount
        {
            get
            {
                return _ReplaceRetryCount;
            }
            set
            {
                _ReplaceRetryCount = Math.Clamp(value, 0, 20);
            }
        }

        #endregion

        #region Private-Members

        private DiskStorageSettings _Disk = new DiskStorageSettings();
        private long _MaxObjectBytes = 5368709120L;
        private int _MaxKeyBytes = 1024;
        private int _MaxLabels = 64;
        private int _MaxLabelLength = 512;
        private int _MaxTags = 64;
        private int _MaxMetadataObjectBytes = 1048576;
        private int _ReplaceRetryCount = 3;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate storage settings.
        /// </summary>
        public StorageSettings()
        {
        }

        #endregion
    }
}
