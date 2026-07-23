namespace PepperX.Sdk.Models
{
    /// <summary>
    /// Machine-readable error classification returned by the server.
    /// </summary>
    public enum ApiErrorEnum
    {
        /// <summary>The request was malformed or failed validation (HTTP 400).</summary>
        BadRequest,

        /// <summary>The requested resource does not exist (HTTP 404).</summary>
        NotFound,

        /// <summary>The resource already exists or a concurrent modification occurred (HTTP 409).</summary>
        Conflict,

        /// <summary>A container could not be deleted because it still holds objects (HTTP 409).</summary>
        NotEmpty,

        /// <summary>A payload or metadata field exceeded a configured limit (HTTP 413).</summary>
        TooLarge,

        /// <summary>The target object is being deleted (HTTP 410).</summary>
        Deleting,

        /// <summary>The operation is not implemented on this surface (HTTP 501).</summary>
        NotImplemented,

        /// <summary>An unexpected server-side error occurred (HTTP 500).</summary>
        InternalError
    }
}
