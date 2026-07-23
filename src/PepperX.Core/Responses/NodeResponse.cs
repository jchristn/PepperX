namespace PepperX.Core.Responses
{
    using System;
    using PepperX.Core.Models;

    /// <summary>
    /// Transport view of a cluster node and its liveness.
    /// </summary>
    public class NodeResponse
    {
        #region Public-Members

        /// <summary>
        /// Node identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Host name reported by the node. May be null.
        /// </summary>
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// UTC time the node started.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time of the node's most recent heartbeat.
        /// </summary>
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Seconds elapsed since the last heartbeat.
        /// </summary>
        public double HeartbeatAgeSeconds { get; set; } = 0;

        /// <summary>
        /// Whether the node is considered alive given the configured dead-after threshold.
        /// </summary>
        public bool IsAlive { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty node response.
        /// </summary>
        public NodeResponse()
        {
        }

        /// <summary>
        /// Build a response from a node record.
        /// </summary>
        /// <param name="node">Source node record.</param>
        /// <param name="deadAfterSeconds">Threshold beyond which a node is considered dead.</param>
        /// <param name="nowUtc">Reference UTC time.</param>
        /// <returns>Node response.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
        public static NodeResponse FromModel(NodeRecord node, int deadAfterSeconds, DateTime nowUtc)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            double age = (nowUtc - node.LastHeartbeatUtc).TotalSeconds;

            return new NodeResponse
            {
                Id = node.Id,
                Hostname = node.Hostname,
                StartedUtc = node.StartedUtc,
                LastHeartbeatUtc = node.LastHeartbeatUtc,
                HeartbeatAgeSeconds = age < 0 ? 0 : age,
                IsAlive = age <= deadAfterSeconds
            };
        }

        #endregion
    }
}
