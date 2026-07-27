namespace PepperX.Server.Api.S3
{
    using System;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using S3ServerLibrary;
    using S3ServerLibrary.S3Objects;

    /// <summary>
    /// Maps PepperX domain exceptions to S3 protocol errors.
    /// </summary>
    public static class S3ErrorMapper
    {
        /// <summary>
        /// Convert an exception into an <see cref="S3Exception"/> with the appropriate S3 error code.
        /// </summary>
        /// <param name="ex">Source exception.</param>
        /// <param name="key">Optional object key for context.</param>
        /// <returns>An S3 exception.</returns>
        public static S3Exception Map(Exception ex, string? key = null)
        {
            if (ex is S3Exception s3) return s3;

            ErrorCode code = ErrorCode.InternalError;

            if (ex is ContainerNotFoundException) code = ErrorCode.NoSuchBucket;
            else if (ex is ObjectNotFoundException) code = ErrorCode.NoSuchKey;
            else if (ex is ContainerNotEmptyException) code = ErrorCode.BucketNotEmpty;
            else if (ex is ObjectAlreadyExistsException) code = ErrorCode.BucketAlreadyExists;
            else if (ex is ObjectTooLargeException) code = ErrorCode.EntityTooLarge;
            else if (ex is NoSuchUploadException) code = ErrorCode.NoSuchUpload;
            else if (ex is InvalidPartException) code = ErrorCode.InvalidPart;
            else if (ex is InvalidPartOrderException) code = ErrorCode.InvalidPartOrder;
            else if (ex is EntityTooSmallException) code = ErrorCode.EntityTooSmall;
            else if (ex is PepperXException pex)
            {
                code = pex.ErrorType switch
                {
                    ApiErrorEnum.NotFound => ErrorCode.NoSuchKey,
                    ApiErrorEnum.Conflict => ErrorCode.BucketAlreadyExists,
                    ApiErrorEnum.NotEmpty => ErrorCode.BucketNotEmpty,
                    ApiErrorEnum.BadRequest => ErrorCode.InvalidArgument,
                    ApiErrorEnum.TooLarge => ErrorCode.EntityTooLarge,
                    _ => ErrorCode.InternalError
                };
            }
            else if (ex is ArgumentException) code = ErrorCode.InvalidArgument;

            return new S3Exception(new Error(code, key));
        }
    }
}
