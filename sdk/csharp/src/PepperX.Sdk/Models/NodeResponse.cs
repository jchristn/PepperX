namespace PepperX.Sdk.Models
{
    using System;

    /// <summary>
    /// A cluster node and its liveness.
    /// </summary>
    public class NodeResponse
    {
        #region Public-Members

        /// <summary>Node identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Host name reported by the node. May be null.</summary>
        public string? Hostname { get; set; } = null;

        /// <summary>UTC time the node started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time of the node's most recent heartbeat.</summary>
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Seconds elapsed since the last heartbeat.</summary>
        public double HeartbeatAgeSeconds { get; set; } = 0;

        /// <summary>Whether the node is considered alive.</summary>
        public bool IsAlive { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty node response.
        /// </summary>
        public NodeResponse()
        {
        }

        #endregion
    }
}
