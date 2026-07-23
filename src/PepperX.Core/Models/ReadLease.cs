namespace PepperX.Core.Models
{
    using System;
    using PepperX.Core.Helpers;

    /// <summary>
    /// A read lease records an in-flight read of an extent. A delete waits until no unexpired lease exists
    /// for the target extent, which is how PepperX guarantees, cluster-wide, that a delete does not destroy
    /// a payload that a reader is still streaming.
    /// </summary>
    public class ReadLease
    {
        #region Public-Members

        /// <summary>
        /// Lease identifier (prefix <c>lse_</c>). Never null or empty.
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
        /// Identifier of the extent being read. Never null or empty.
        /// </summary>
        public string ExtentId
        {
            get
            {
                return _ExtentId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ExtentId));
                _ExtentId = value;
            }
        }

        /// <summary>
        /// Identifier of the node holding the lease. Never null or empty.
        /// </summary>
        public string NodeId
        {
            get
            {
                return _NodeId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(NodeId));
                _NodeId = value;
            }
        }

        /// <summary>
        /// UTC time the lease was acquired.
        /// </summary>
        public DateTime AcquiredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the lease expires. A reader renews this while streaming; an abandoned lease is reclaimed
        /// after it expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateLeaseId();
        private string _ExtentId = String.Empty;
        private string _NodeId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a read lease. The identifier defaults to a generated value.
        /// </summary>
        public ReadLease()
        {
        }

        #endregion
    }
}
