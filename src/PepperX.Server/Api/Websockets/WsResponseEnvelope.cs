namespace PepperX.Server.Api.Websockets
{
    using PepperX.Core.Responses;

    /// <summary>
    /// A response envelope over the WebSocket protocol, correlated to a request by <see cref="RequestId"/>.
    /// </summary>
    public class WsResponseEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Correlation identifier echoed from the request. May be null for unparseable requests.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Whether the operation succeeded.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Equivalent HTTP status code.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Error payload when the operation failed; otherwise null.
        /// </summary>
        public ApiErrorResponse? Error { get; set; } = null;

        /// <summary>
        /// Operation result, when applicable.
        /// </summary>
        public object? Result { get; set; } = null;

        /// <summary>
        /// Base64-encoded payload, for object reads and writes.
        /// </summary>
        public string? DataBase64 { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty response envelope.
        /// </summary>
        public WsResponseEnvelope()
        {
        }

        #endregion
    }
}
