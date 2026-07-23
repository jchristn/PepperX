namespace PepperX.Sdk.Models
{
    using System.Text.Json;

    /// <summary>
    /// A response envelope from the PepperX WebSocket surface, correlated to a request by
    /// <see cref="RequestId"/>.
    /// </summary>
    public class WsResponseEnvelope
    {
        #region Public-Members

        /// <summary>Correlation identifier echoed from the request. Null for unparseable requests.</summary>
        public string? RequestId { get; set; } = null;

        /// <summary>Whether the operation succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Equivalent HTTP status code.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>Error payload when the operation failed; otherwise null.</summary>
        public ApiErrorResponse? Error { get; set; } = null;

        /// <summary>Operation result as raw JSON, when applicable.</summary>
        public JsonElement? Result { get; set; } = null;

        /// <summary>Base64-encoded payload, for object reads.</summary>
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
