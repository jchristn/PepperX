namespace PepperX.Sdk.Models
{
    using System;

    /// <summary>
    /// The result of writing (creating or replacing) an object.
    /// </summary>
    public class ObjectWriteResponse
    {
        #region Public-Members

        /// <summary>Identifier of the extent that now backs the key.</summary>
        public string ExtentId { get; set; } = String.Empty;

        /// <summary>Object key.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>Owning container identifier.</summary>
        public string ContainerId { get; set; } = String.Empty;

        /// <summary>Payload size in bytes.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>Lowercase hex SHA-256 of the payload.</summary>
        public string Sha256 { get; set; } = String.Empty;

        /// <summary>Content type of the payload. May be null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Whether the write replaced an existing object.</summary>
        public bool Replaced { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty write response.
        /// </summary>
        public ObjectWriteResponse()
        {
        }

        #endregion
    }
}
