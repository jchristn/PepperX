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
