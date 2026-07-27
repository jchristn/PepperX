namespace PepperX.Core.Helpers
{
    /// <summary>
    /// Generates PepperX application identifiers as K-sortable, prefixed strings.
    /// K-sortable ordering means identifiers sort in creation order, which enables
    /// keyset pagination on the identifier column.
    /// </summary>
    public static class IdGenerator
    {
        #region Private-Members

        private static readonly PrettyId.IdGenerator _Generator = new PrettyId.IdGenerator();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a container identifier (prefix <c>ctr_</c>).
        /// </summary>
        /// <returns>Container identifier.</returns>
        public static string GenerateContainerId()
        {
            return _Generator.GenerateKSortable(Constants.ContainerIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an extent identifier (prefix <c>ext_</c>).
        /// </summary>
        /// <returns>Extent identifier.</returns>
        public static string GenerateExtentId()
        {
            return _Generator.GenerateKSortable(Constants.ExtentIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a node identifier (prefix <c>nod_</c>).
        /// </summary>
        /// <returns>Node identifier.</returns>
        public static string GenerateNodeId()
        {
            return _Generator.GenerateKSortable(Constants.NodeIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a read lease identifier (prefix <c>lse_</c>).
        /// </summary>
        /// <returns>Read lease identifier.</returns>
        public static string GenerateLeaseId()
        {
            return _Generator.GenerateKSortable(Constants.LeaseIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate a request history entry identifier (prefix <c>req_</c>).
        /// </summary>
        /// <returns>Request history identifier.</returns>
        public static string GenerateRequestHistoryId()
        {
            return _Generator.GenerateKSortable(Constants.RequestHistoryIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an S3 multipart upload identifier (prefix <c>mpu_</c>). This value is returned to
        /// clients as the opaque S3 UploadId.
        /// </summary>
        /// <returns>Multipart upload identifier.</returns>
        public static string GenerateMultipartUploadId()
        {
            return _Generator.GenerateKSortable(Constants.MultipartUploadIdPrefix, Constants.IdLength);
        }

        /// <summary>
        /// Generate an S3 multipart upload part identifier (prefix <c>mpp_</c>).
        /// </summary>
        /// <returns>Multipart part identifier.</returns>
        public static string GenerateMultipartPartId()
        {
            return _Generator.GenerateKSortable(Constants.MultipartPartIdPrefix, Constants.IdLength);
        }

        #endregion
    }
}
