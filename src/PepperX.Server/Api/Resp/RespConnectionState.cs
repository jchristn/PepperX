namespace PepperX.Server.Api.Resp
{
    /// <summary>
    /// Per-connection RESP state: the selected database index (which maps to a container), the negotiated
    /// protocol version, and the client name.
    /// </summary>
    public sealed class RespConnectionState
    {
        #region Public-Members

        /// <summary>
        /// Selected database index. Default 0.
        /// </summary>
        public int DatabaseIndex { get; set; } = 0;

        /// <summary>
        /// Whether the connection negotiated RESP3 (via HELLO 3). Default false (RESP2).
        /// </summary>
        public bool Resp3 { get; set; } = false;

        /// <summary>
        /// Client name set via CLIENT SETNAME. Default empty.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate default connection state.
        /// </summary>
        public RespConnectionState()
        {
        }

        #endregion
    }
}
