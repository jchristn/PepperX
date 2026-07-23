namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a referenced container does not exist. Maps to HTTP 404.
    /// </summary>
    public class ContainerNotFoundException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a container name or identifier.
        /// </summary>
        /// <param name="container">Container name or identifier that was not found.</param>
        public ContainerNotFoundException(string container)
            : base(ApiErrorEnum.NotFound, 404, "Container '" + container + "' was not found.")
        {
        }

        #endregion
    }
}
