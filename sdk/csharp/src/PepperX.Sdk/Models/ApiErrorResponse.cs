namespace PepperX.Sdk.Models
{
    using System;

    /// <summary>
    /// Error payload returned by the server.
    /// </summary>
    public class ApiErrorResponse
    {
        #region Public-Members

        /// <summary>Machine-readable error classification.</summary>
        public ApiErrorEnum Error { get; set; } = ApiErrorEnum.InternalError;

        /// <summary>Human-readable error message.</summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>HTTP status code associated with the error.</summary>
        public int StatusCode { get; set; } = 500;

        /// <summary>Optional additional context. May be null.</summary>
        public object? Context { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty error response.
        /// </summary>
        public ApiErrorResponse()
        {
        }

        #endregion
    }
}
