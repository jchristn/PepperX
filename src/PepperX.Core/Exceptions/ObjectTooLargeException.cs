namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a payload or metadata field exceeds a configured size limit. Maps to HTTP 413.
    /// </summary>
    public class ObjectTooLargeException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Human-readable message describing the limit that was exceeded.</param>
        public ObjectTooLargeException(string message)
            : base(ApiErrorEnum.TooLarge, 413, message)
        {
        }

        #endregion
    }
}
