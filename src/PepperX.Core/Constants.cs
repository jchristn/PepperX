namespace PepperX.Core
{
    /// <summary>
    /// Product-wide constant values: identity, identifier prefixes, content types, and protocol header names.
    /// These are fixed structural values. Tunable operational values belong in the settings classes.
    /// </summary>
    public static class Constants
    {
        #region Identity

        /// <summary>
        /// Product name.
        /// </summary>
        public const string ProductName = "PepperX";

        /// <summary>
        /// Product version.
        /// </summary>
        public const string ProductVersion = "0.1.0";

        #endregion

        #region Identifier-Prefixes

        /// <summary>
        /// Identifier prefix for containers.
        /// </summary>
        public const string ContainerIdPrefix = "ctr_";

        /// <summary>
        /// Identifier prefix for extents.
        /// </summary>
        public const string ExtentIdPrefix = "ext_";

        /// <summary>
        /// Identifier prefix for cluster nodes.
        /// </summary>
        public const string NodeIdPrefix = "nod_";

        /// <summary>
        /// Identifier prefix for read leases.
        /// </summary>
        public const string LeaseIdPrefix = "lse_";

        /// <summary>
        /// Identifier prefix for request history entries.
        /// </summary>
        public const string RequestHistoryIdPrefix = "req_";

        /// <summary>
        /// Identifier prefix for S3 multipart uploads. This value is the opaque S3 UploadId.
        /// </summary>
        public const string MultipartUploadIdPrefix = "mpu_";

        /// <summary>
        /// Identifier prefix for S3 multipart upload parts.
        /// </summary>
        public const string MultipartPartIdPrefix = "mpp_";

        /// <summary>
        /// Total length, including prefix, of generated identifiers.
        /// </summary>
        public const int IdLength = 24;

        #endregion

        #region Content-Types

        /// <summary>
        /// JSON content type.
        /// </summary>
        public const string JsonContentType = "application/json";

        /// <summary>
        /// Default binary content type applied when a caller does not specify one.
        /// </summary>
        public const string OctetStreamContentType = "application/octet-stream";

        #endregion

        #region Header-Names

        /// <summary>
        /// Request/response header carrying a comma-separated list of labels.
        /// </summary>
        public const string LabelsHeader = "x-pepperx-labels";

        /// <summary>
        /// Request/response header carrying URL-encoded key=value tag pairs.
        /// </summary>
        public const string TagsHeader = "x-pepperx-tags";

        /// <summary>
        /// Request/response header carrying a base64-encoded JSON metadata object.
        /// </summary>
        public const string ObjectHeader = "x-pepperx-object";

        /// <summary>
        /// Response header indicating a metadata object exists but was too large to inline as a header.
        /// </summary>
        public const string ObjectAvailableHeader = "x-pepperx-object-available";

        /// <summary>
        /// Response header carrying the extent identifier.
        /// </summary>
        public const string ExtentIdHeader = "x-pepperx-extent-id";

        /// <summary>
        /// Response header carrying the payload SHA-256 (hex).
        /// </summary>
        public const string Sha256Header = "x-pepperx-sha256";

        /// <summary>
        /// Response header carrying the payload MD5 (hex). Absent for objects written before MD5 was recorded.
        /// </summary>
        public const string Md5Header = "x-pepperx-md5";

        #endregion

        #region Banner

        /// <summary>
        /// ASCII console banner shown at server startup.
        /// </summary>
        public const string Logo =
            "\r\n" +
            "  ___                       __  __\r\n" +
            " | _ \\___ _ __ _ __  ___ _ _\\ \\/ /\r\n" +
            " |  _/ -_) '_ \\ '_ \\/ -_) '_|>  < \r\n" +
            " |_| \\___| .__/ .__/\\___|_| /_/\\_\\\r\n" +
            "         |_|  |_|                  \r\n";

        #endregion
    }
}
