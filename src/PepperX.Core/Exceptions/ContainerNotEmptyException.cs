namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a container delete is attempted while the container still holds objects and force was not
    /// requested. Maps to HTTP 409.
    /// </summary>
    public class ContainerNotEmptyException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a container.
        /// </summary>
        /// <param name="container">Container name.</param>
        /// <param name="objectCount">Number of objects still in the container.</param>
        public ContainerNotEmptyException(string container, long objectCount)
            : base(ApiErrorEnum.NotEmpty, 409, "Container '" + container + "' is not empty (" + objectCount + " object(s)). Use force to delete its contents.")
        {
        }

        #endregion
    }
}
