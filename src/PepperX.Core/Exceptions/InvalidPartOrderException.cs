namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when the parts in a complete-multipart-upload request are not in strictly ascending order or
    /// contain duplicates. Maps to HTTP 400 and the S3 code InvalidPartOrder.
    /// </summary>
    public class InvalidPartOrderException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        public InvalidPartOrderException()
            : base(ApiErrorEnum.BadRequest, 400, "The list of parts was not in ascending order; parts must be ordered by ascending part number with no duplicates.")
        {
        }

        #endregion
    }
}
