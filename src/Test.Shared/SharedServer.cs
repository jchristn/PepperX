namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A single in-process PepperX server with every protocol listener enabled, started lazily on first use
    /// and shared by all protocol suites. Because initialization is lazy rather than suite-scoped, the
    /// descriptors run correctly under every runner, including runners that execute each descriptor
    /// independently and therefore do not invoke suite lifecycle hooks.
    /// This type is thread-safe.
    /// </summary>
    public static class SharedServer
    {
        #region Private-Members

        private static readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private static RestTestServer? _Instance;
        private static bool _ExitHookRegistered;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the shared server, starting it on first use.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The shared server.</returns>
        public static async Task<RestTestServer> GetAsync(CancellationToken token = default)
        {
            if (_Instance != null) return _Instance;

            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Instance == null)
                {
                    _Instance = await RestTestServer.StartAsync(true, true, true, true, token).ConfigureAwait(false);
                    RegisterExitHook();
                }
            }
            finally
            {
                _Gate.Release();
            }

            return _Instance;
        }

        /// <summary>
        /// Stop and dispose the shared server if it is running.
        /// </summary>
        /// <returns>Task.</returns>
        public static async Task ShutdownAsync()
        {
            await _Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_Instance != null)
                {
                    await _Instance.DisposeAsync().ConfigureAwait(false);
                    _Instance = null;
                }
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion

        #region Private-Methods

        private static void RegisterExitHook()
        {
            if (_ExitHookRegistered) return;
            _ExitHookRegistered = true;

            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                try
                {
                    ShutdownAsync().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // Best-effort cleanup at process exit.
                }
            };
        }

        #endregion
    }
}
