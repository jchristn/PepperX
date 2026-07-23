namespace PepperX.Sdk
{
    using System;
    using PepperX.Sdk.Models;

    /// <summary>
    /// Thrown when a PepperX server returns an error response. Carries the server's typed classification and
    /// status code so callers can branch without parsing message text.
    /// </summary>
    public class PepperXException : Exception
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable error classification reported by the server.
        /// </summary>
        public ApiErrorEnum ErrorType { get; }

        /// <summary>
        /// HTTP status code reported by the server.
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Raw response body, when available.
        /// </summary>
        public string? Body { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="errorType">Error classification.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="body">Raw response body, when available.</param>
        public PepperXException(ApiErrorEnum errorType, int statusCode, string message, string? body = null)
            : base(message)
        {
            ErrorType = errorType;
            StatusCode = statusCode;
            Body = body;
        }

        #endregion
    }
}
