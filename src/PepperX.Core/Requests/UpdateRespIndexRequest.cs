namespace PepperX.Core.Requests
{
    /// <summary>
    /// A request to assign or clear a container's RESP (Redis) database index. A null <see cref="Index"/>
    /// clears the mapping; a non-negative value claims that index, which must be unique across containers.
    /// </summary>
    public class UpdateRespIndexRequest
    {
        #region Public-Members

        /// <summary>
        /// The RESP database index to claim, or null to clear the mapping. Must be non-negative when set.
        /// </summary>
        public int? Index { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a default request.
        /// </summary>
        public UpdateRespIndexRequest()
        {
        }

        #endregion
    }
}
