namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a conditional replace loses a race to another writer and the caller should retry. Maps to
    /// HTTP 409.
    /// </summary>
    public class ConcurrentModificationException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Human-readable message.</param>
        public ConcurrentModificationException(string message)
            : base(ApiErrorEnum.Conflict, 409, message)
        {
        }

        #endregion
    }
}
