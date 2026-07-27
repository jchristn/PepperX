namespace PepperX.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request body to create a container.
    /// </summary>
    public class ContainerCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Container name. Must satisfy the container naming rules (3 to 63 lowercase alphanumeric or hyphen
        /// characters, starting and ending with a letter or digit).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Optional container tags. May be null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Optional cache settings. When null, the container is created with the enabled-by-default cache
        /// configuration (LRU with reasonable sizes). Supply an explicit block to override, including one
        /// with <see cref="UpdateCacheSettingsRequest.Enabled"/> set to false to opt out.
        /// </summary>
        public UpdateCacheSettingsRequest? Cache { get; set; } = null;

        /// <summary>
        /// Optional RESP database index to claim for this container. When set, a Redis client issuing
        /// <c>SELECT n</c> with this index addresses this container. Must be unique across containers; a
        /// conflict fails the create with 409. Null (the default) leaves the container unaddressable by an
        /// explicit RESP index.
        /// </summary>
        public int? RespDatabaseIndex { get; set; } = null;

        /// <summary>
        /// Optional per-container expiry, in days, for in-progress multipart uploads. When set (1..365), an
        /// upload started in this container expires this many days after it begins, overriding the
        /// system-wide <c>S3.MultipartUploadExpiryDays</c> default. Null (the default) inherits the
        /// system-wide value.
        /// </summary>
        public int? MultipartUploadExpiryDays { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a container create request.
        /// </summary>
        public ContainerCreateRequest()
        {
        }

        #endregion
    }
}
