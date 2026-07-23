namespace PepperX.Core.Responses
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Standard error payload returned by the native protocol surfaces.
    /// </summary>
    public class ApiErrorResponse
    {
        #region Public-Members

        /// <summary>
        /// Machine-readable error classification.
        /// </summary>
        public ApiErrorEnum Error { get; set; } = ApiErrorEnum.InternalError;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// HTTP status code associated with the error.
        /// </summary>
        public int StatusCode { get; set; } = 500;

        /// <summary>
        /// Optional additional context (for example the offending field or resource). May be null.
        /// </summary>
        public object? Context { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty error response.
        /// </summary>
        public ApiErrorResponse()
        {
        }

        /// <summary>
        /// Instantiate an error response.
        /// </summary>
        /// <param name="error">Error classification.</param>
        /// <param name="message">Human-readable message.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="context">Optional context.</param>
        public ApiErrorResponse(ApiErrorEnum error, string message, int statusCode, object? context = null)
        {
            Error = error;
            Message = message ?? String.Empty;
            StatusCode = statusCode;
            Context = context;
        }

        #endregion
    }
}
