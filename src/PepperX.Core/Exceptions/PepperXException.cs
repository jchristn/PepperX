namespace PepperX.Core.Exceptions
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Base type for PepperX domain exceptions. Each carries an <see cref="ApiErrorEnum"/> classification and
    /// an HTTP status code so protocol surfaces can translate failures consistently.
    /// </summary>
    public class PepperXException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Error classification.
        /// </summary>
        public ApiErrorEnum ErrorType { get; }

        /// <summary>
        /// HTTP status code associated with the error.
        /// </summary>
        public int StatusCode { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a PepperX exception.
        /// </summary>
        /// <param name="errorType">Error classification.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="inner">Optional inner exception.</param>
        public PepperXException(ApiErrorEnum errorType, int statusCode, string message, Exception? inner = null)
            : base(message, inner)
        {
            ErrorType = errorType;
            StatusCode = statusCode;
        }

        #endregion
    }
}
