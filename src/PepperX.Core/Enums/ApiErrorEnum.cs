namespace PepperX.Core.Enums
{
    /// <summary>
    /// Machine-readable error classification returned in error responses across every protocol surface.
    /// </summary>
    public enum ApiErrorEnum
    {
        /// <summary>
        /// The request was malformed or failed validation. Maps to HTTP 400.
        /// </summary>
        BadRequest,

        /// <summary>
        /// The requested resource does not exist. Maps to HTTP 404.
        /// </summary>
        NotFound,

        /// <summary>
        /// The resource already exists or a concurrent modification conflict occurred. Maps to HTTP 409.
        /// </summary>
        Conflict,

        /// <summary>
        /// A container could not be deleted because it still contains objects. Maps to HTTP 409.
        /// </summary>
        NotEmpty,

        /// <summary>
        /// The payload or a metadata field exceeded a configured size limit. Maps to HTTP 413.
        /// </summary>
        TooLarge,

        /// <summary>
        /// The target extent is being deleted and is no longer readable. Maps to HTTP 410.
        /// </summary>
        Deleting,

        /// <summary>
        /// The operation is not implemented on this protocol surface. Maps to HTTP 501.
        /// </summary>
        NotImplemented,

        /// <summary>
        /// An unexpected server-side error occurred. Maps to HTTP 500.
        /// </summary>
        InternalError
    }
}
