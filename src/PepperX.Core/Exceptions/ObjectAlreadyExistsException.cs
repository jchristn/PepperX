namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a no-overwrite write targets a key that already exists. Maps to HTTP 409.
    /// </summary>
    public class ObjectAlreadyExistsException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a container and key.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key that already exists.</param>
        public ObjectAlreadyExistsException(string container, string key)
            : base(ApiErrorEnum.Conflict, 409, "Object '" + key + "' already exists in container '" + container + "'.")
        {
        }

        #endregion
    }
}
