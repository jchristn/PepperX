namespace PepperX.Core.Exceptions
{
    using PepperX.Core.Enums;

    /// <summary>
    /// Thrown when a part listed in a complete-multipart-upload request is missing or its supplied ETag does
    /// not match the staged part's MD5. Maps to HTTP 400 and the S3 code InvalidPart.
    /// </summary>
    public class InvalidPartException : PepperXException
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception for a part number.
        /// </summary>
        /// <param name="partNumber">The offending part number.</param>
        public InvalidPartException(int partNumber)
            : base(ApiErrorEnum.BadRequest, 400, "Part " + partNumber + " is missing or its ETag does not match the staged part.")
        {
        }

        #endregion
    }
}
