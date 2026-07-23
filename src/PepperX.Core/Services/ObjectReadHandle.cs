namespace PepperX.Core.Services
{
    using System;
    using System.Threading.Tasks;
    using PepperX.Core.Models;
    using PepperX.Core.Storage;

    /// <summary>
    /// A handle to an in-progress object read. It exposes the resolved extent and an open payload stream, and
    /// holds the read protection (a database lease or an in-process read lock) until disposed. Callers must
    /// dispose the handle when the read completes so a pending delete can proceed. This type is not
    /// thread-safe; use one handle per read.
    /// </summary>
    public sealed class ObjectReadHandle : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// The resolved extent.
        /// </summary>
        public Extent Extent
        {
            get
            {
                return _Extent;
            }
        }

        /// <summary>
        /// The open payload stream (full object or requested range).
        /// </summary>
        public ExtentPayloadStream Payload
        {
            get
            {
                return _Payload;
            }
        }

        #endregion

        #region Private-Members

        private readonly Extent _Extent;
        private readonly ExtentPayloadStream _Payload;
        private readonly Func<ValueTask> _Release;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a read handle.
        /// </summary>
        /// <param name="extent">Resolved extent.</param>
        /// <param name="payload">Open payload stream.</param>
        /// <param name="release">Release action invoked on dispose (release lease/lock and stop any renewal).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ObjectReadHandle(Extent extent, ExtentPayloadStream payload, Func<ValueTask> release)
        {
            _Extent = extent ?? throw new ArgumentNullException(nameof(extent));
            _Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            _Release = release ?? throw new ArgumentNullException(nameof(release));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose the payload stream and release the read protection.
        /// </summary>
        /// <returns>Value task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;

            await _Payload.DisposeAsync().ConfigureAwait(false);
            await _Release().ConfigureAwait(false);
        }

        #endregion
    }
}
