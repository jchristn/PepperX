namespace PepperX.Core.Exceptions
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when an extent file fails format or checksum validation. Maps to HTTP 500.
    /// </summary>
    public class ExtentCorruptException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Human-readable message describing the corruption.</param>
        /// <param name="inner">Optional inner exception.</param>
        public ExtentCorruptException(string message, Exception? inner = null)
            : base(ApiErrorEnum.InternalError, 500, message, inner)
        {
        }

        #endregion
    }
}
