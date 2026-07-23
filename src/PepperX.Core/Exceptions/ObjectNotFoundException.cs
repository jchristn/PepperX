namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a referenced object key does not exist in a container. Maps to HTTP 404.
    /// </summary>
    public class ObjectNotFoundException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a container and key.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="key">Object key that was not found.</param>
        public ObjectNotFoundException(string container, string key)
            : base(ApiErrorEnum.NotFound, 404, "Object '" + key + "' was not found in container '" + container + "'.")
        {
        }

        #endregion
    }
}
