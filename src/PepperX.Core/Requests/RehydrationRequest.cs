namespace PepperX.Core.Requests
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Request body to run a rehydration reconciling the metadata database with raw extent storage.
    /// </summary>
    public class RehydrationRequest
    {
        #region Public-Members

        /// <summary>
        /// Reconciliation mode. Default <see cref="RehydrationModeEnum.Verify"/> (report only).
        /// </summary>
        public RehydrationModeEnum Mode { get; set; } = RehydrationModeEnum.Verify;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a rehydration request.
        /// </summary>
        public RehydrationRequest()
        {
        }

        #endregion
    }
}
