namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a non-final part is smaller than the configured minimum part size at completion. Maps to
    /// HTTP 400 and the S3 code EntityTooSmall.
    /// </summary>
    public class EntityTooSmallException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a part.
        /// </summary>
        /// <param name="partNumber">The offending part number.</param>
        /// <param name="minBytes">The configured minimum part size in bytes.</param>
        public EntityTooSmallException(int partNumber, long minBytes)
            : base(ApiErrorEnum.BadRequest, 400, "Part " + partNumber + " is smaller than the minimum allowed size of " + minBytes + " bytes; only the last part may be smaller.")
        {
        }

        #endregion
    }
}
