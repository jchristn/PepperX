namespace PepperX.Core.Services
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using PepperX.Core.Settings;
    using PepperX.Core.Telemetry;
    using SyslogLogging;

    /// <summary>
    /// Periodically records this node's heartbeat so the cluster can identify live nodes and reclaim leases
    /// held by dead ones.
    /// </summary>
    public sealed class NodeHeartbeatService : IDisposable
    {
        #region Private-Members

        private readonly string _Header = "[NodeHeartbeatService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly ClusterSettings _Cluster;
        private readonly LoggingModule? _Logging;
        private readonly NodeRecord _Node;
        private Timer? _Timer;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the heartbeat service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">Application settings.</param>
        /// <param name="nodeId">This node's identifier.</param>
        /// <param name="hostname">This node's host name.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public NodeHeartbeatService(IMetadataDatabaseDriver db, PepperXSettings settings, string nodeId, string? hostname, LoggingModule? logging = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cluster = settings.Cluster;
            _Logging = logging;
            _Node = new NodeRecord
            {
                Id = String.IsNullOrEmpty(nodeId) ? throw new ArgumentNullException(nameof(nodeId)) : nodeId,
                Hostname = hostname,
                StartedUtc = DateTime.UtcNow
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the first heartbeat and start the periodic timer.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            await BeatAsync(token).ConfigureAwait(false);
            int intervalMs = _Cluster.HeartbeatIntervalSeconds * 1000;
            _Timer = new Timer(_ => Beat(), null, intervalMs, intervalMs);
        }

        /// <summary>
        /// Stop the heartbeat timer.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Timer?.Dispose();
            _Timer = null;
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void Beat()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await BeatAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging?.Warn(_Header + "heartbeat failed: " + ex.Message);
                }
            });
        }

        private async Task BeatAsync(CancellationToken token)
        {
            using Activity? __act = PepperXTelemetry.StartActivity("heartbeat.beat", ActivityKind.Internal);
            try
            {
                _Node.LastHeartbeatUtc = DateTime.UtcNow;
                await _Db.Nodes.UpsertHeartbeatAsync(_Node, token).ConfigureAwait(false);
                PepperXTelemetry.RecordHeartbeat(true);
            }
            catch (Exception __ex)
            {
                PepperXTelemetry.RecordHeartbeat(false);
                PepperXTelemetry.RecordException(__act, __ex);
                throw;
            }
        }

        #endregion
    }
}
