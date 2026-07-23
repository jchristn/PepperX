namespace PepperX.Core.Models
{
    using System;
    using PepperX.Core.Helpers;

    /// <summary>
    /// A registered cluster node and its most recent heartbeat. Nodes are stateless protocol handlers;
    /// this record exists so the cluster can identify live nodes and reclaim leases held by dead ones.
    /// </summary>
    public class NodeRecord
    {
        #region Public-Members

        /// <summary>
        /// Node identifier (prefix <c>nod_</c>). Never null or empty.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Host name reported by the node. May be null.
        /// </summary>
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// UTC time the node process started.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time of the node's most recent heartbeat.
        /// </summary>
        public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the node record was created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateNodeId();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a node record. The identifier defaults to a generated value.
        /// </summary>
        public NodeRecord()
        {
        }

        #endregion
    }
}
