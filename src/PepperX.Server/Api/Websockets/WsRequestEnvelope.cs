namespace PepperX.Server.Api.Websockets
{
    using System;

    /// <summary>
    /// A request envelope over the WebSocket protocol. Multiple requests may be in flight on one connection;
    /// responses are correlated by <see cref="RequestId"/>.
    /// </summary>
    public class WsRequestEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Caller-supplied correlation identifier.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// The operation to perform.
        /// </summary>
        public WsOperationEnum Operation { get; set; } = WsOperationEnum.Health;

        /// <summary>
        /// Container name, when the operation targets a container.
        /// </summary>
        public string? Container { get; set; } = null;

        /// <summary>
        /// Object key, when the operation targets an object.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Operation-specific request body (bound to the operation's request type).
        /// </summary>
        public object? Body { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty request envelope.
        /// </summary>
        public WsRequestEnvelope()
        {
        }

        #endregion
    }
}
