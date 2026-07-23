namespace PepperX.Core.Database
{
    using System;
    using PepperX.Core.Models;

    /// <summary>
    /// The result of atomically acquiring a read lease on an active extent: the extent that was resolved and
    /// the identifier of the lease that now protects it from deletion.
    /// </summary>
    public class LeaseAcquisition
    {
        #region Public-Members

        /// <summary>
        /// The resolved active extent.
        /// </summary>
        public Extent Extent
        {
            get
            {
                return _Extent;
            }
        }

        /// <summary>
        /// The identifier of the acquired lease.
        /// </summary>
        public string LeaseId
        {
            get
            {
                return _LeaseId;
            }
        }

        #endregion

        #region Private-Members

        private readonly Extent _Extent;
        private readonly string _LeaseId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a lease acquisition.
        /// </summary>
        /// <param name="extent">Resolved extent.</param>
        /// <param name="leaseId">Acquired lease identifier.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public LeaseAcquisition(Extent extent, string leaseId)
        {
            _Extent = extent ?? throw new ArgumentNullException(nameof(extent));
            _LeaseId = leaseId ?? throw new ArgumentNullException(nameof(leaseId));
        }

        #endregion
    }
}
