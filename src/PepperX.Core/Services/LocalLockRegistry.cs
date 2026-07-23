namespace PepperX.Core.Services
{
    using System;
    using System.Threading;

    /// <summary>
    /// A fixed pool of reader/writer locks addressed by a string key, used by the Local delete-coordination
    /// mode to make deletes wait for in-flight reads within a single process. Distinct keys may share a lock
    /// (which only adds serialization, never incorrectness); the same key always maps to the same lock.
    /// This type is thread-safe.
    /// </summary>
    public sealed class LocalLockRegistry : IDisposable
    {
        #region Private-Members

        private readonly ReaderWriterLockSlim[] _Locks;
        private readonly int _Count;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the registry.
        /// </summary>
        /// <param name="poolSize">Number of underlying locks. Clamped to the range 16 to 65536. Default 1024.</param>
        public LocalLockRegistry(int poolSize = 1024)
        {
            _Count = Math.Clamp(poolSize, 16, 65536);
            _Locks = new ReaderWriterLockSlim[_Count];
            for (int i = 0; i < _Count; i++) _Locks[i] = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enter a read lock for a key. Balance with <see cref="ExitRead"/>.
        /// </summary>
        /// <param name="key">Lock key.</param>
        public void EnterRead(string key)
        {
            LockFor(key).EnterReadLock();
        }

        /// <summary>
        /// Exit a read lock for a key.
        /// </summary>
        /// <param name="key">Lock key.</param>
        public void ExitRead(string key)
        {
            LockFor(key).ExitReadLock();
        }

        /// <summary>
        /// Enter a write lock for a key, waiting for in-flight readers. Balance with <see cref="ExitWrite"/>.
        /// </summary>
        /// <param name="key">Lock key.</param>
        public void EnterWrite(string key)
        {
            LockFor(key).EnterWriteLock();
        }

        /// <summary>
        /// Exit a write lock for a key.
        /// </summary>
        /// <param name="key">Lock key.</param>
        public void ExitWrite(string key)
        {
            LockFor(key).ExitWriteLock();
        }

        /// <summary>
        /// Dispose all underlying locks.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            for (int i = 0; i < _Count; i++) _Locks[i].Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private ReaderWriterLockSlim LockFor(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            uint hash = 2166136261;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619;
            }
            return _Locks[hash % (uint)_Count];
        }

        #endregion
    }
}
