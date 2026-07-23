namespace PepperX.Sdk.Models
{
    using System;

    /// <summary>
    /// The result of reading an object into memory: its payload plus the identifying headers the server
    /// returned. Use the streaming read overload for large objects.
    /// </summary>
    public class ObjectReadResult
    {
        #region Public-Members

        /// <summary>Payload bytes. Never null.</summary>
        public byte[] Data
        {
            get
            {
                return _Data;
            }
            set
            {
                _Data = value ?? Array.Empty<byte>();
            }
        }

        /// <summary>Content type reported by the server. May be null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Identifier of the extent that served the read. May be null.</summary>
        public string? ExtentId { get; set; } = null;

        /// <summary>Lowercase hex SHA-256 of the payload. May be null.</summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>Whether the object carries a freeform metadata object.</summary>
        public bool HasMetadataObject { get; set; } = false;

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty read result.
        /// </summary>
        public ObjectReadResult()
        {
        }

        #endregion
    }
}
