namespace PepperX.Core.Settings
{
    using System;
    using PepperX.Core.Enums;

    /// <summary>
    /// Cluster coordination configuration governing read leases, heartbeats, and delete draining.
    /// </summary>
    public class ClusterSettings
    {
        #region Public-Members

        /// <summary>
        /// Explicit node identifier. When null, the node generates one at startup. Default null.
        /// </summary>
        public string? NodeId { get; set; } = null;

        /// <summary>
        /// Delete coordination strategy. Default <see cref="DeleteCoordinationModeEnum.Cluster"/>.
        /// </summary>
        public DeleteCoordinationModeEnum DeleteCoordinationMode { get; set; } = DeleteCoordinationModeEnum.Cluster;

        /// <summary>
        /// Read lease time-to-live in seconds. A read renews its lease while streaming; an abandoned lease is
        /// reclaimed after this interval. Clamped to the range 5 to 3600. Default 30.
        /// </summary>
        public int ReadLeaseTtlSeconds
        {
            get
            {
                return _ReadLeaseTtlSeconds;
            }
            set
            {
                _ReadLeaseTtlSeconds = Math.Clamp(value, 5, 3600);
            }
        }

        /// <summary>
        /// Interval in seconds between node heartbeats. Clamped to the range 1 to 300. Default 5.
        /// </summary>
        public int HeartbeatIntervalSeconds
        {
            get
            {
                return _HeartbeatIntervalSeconds;
            }
            set
            {
                _HeartbeatIntervalSeconds = Math.Clamp(value, 1, 300);
            }
        }

        /// <summary>
        /// Seconds after a node's last heartbeat beyond which it is considered dead and its leases may be
        /// reclaimed. Clamped to the range 10 to 3600. Default 60.
        /// </summary>
        public int NodeDeadAfterSeconds
        {
            get
            {
                return _NodeDeadAfterSeconds;
            }
            set
            {
                _NodeDeadAfterSeconds = Math.Clamp(value, 10, 3600);
            }
        }

        /// <summary>
        /// Poll interval in milliseconds while a delete drains in-flight reads. Clamped to the range 10 to
        /// 5000. Default 50.
        /// </summary>
        public int DeleteDrainPollMs
        {
            get
            {
                return _DeleteDrainPollMs;
            }
            set
            {
                _DeleteDrainPollMs = Math.Clamp(value, 10, 5000);
            }
        }

        /// <summary>
        /// Maximum seconds to wait for reads to drain before handing physical destruction to the janitor.
        /// Clamped to the range 1 to 3600. Default 120.
        /// </summary>
        public int DeleteDrainTimeoutSeconds
        {
            get
            {
                return _DeleteDrainTimeoutSeconds;
            }
            set
            {
                _DeleteDrainTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        /// <summary>
        /// Interval in seconds between janitor sweeps (orphan cleanup, lease purge, history pruning). Clamped
        /// to the range 5 to 3600. Default 60.
        /// </summary>
        public int JanitorIntervalSeconds
        {
            get
            {
                return _JanitorIntervalSeconds;
            }
            set
            {
                _JanitorIntervalSeconds = Math.Clamp(value, 5, 3600);
            }
        }

        #endregion

        #region Private-Members

        private int _ReadLeaseTtlSeconds = 30;
        private int _HeartbeatIntervalSeconds = 5;
        private int _NodeDeadAfterSeconds = 60;
        private int _DeleteDrainPollMs = 50;
        private int _DeleteDrainTimeoutSeconds = 120;
        private int _JanitorIntervalSeconds = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate cluster settings.
        /// </summary>
        public ClusterSettings()
        {
        }

        #endregion
    }
}
