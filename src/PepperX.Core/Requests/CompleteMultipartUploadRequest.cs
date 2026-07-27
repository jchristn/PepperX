namespace PepperX.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// A protocol-neutral complete-multipart-upload request. The S3 protocol handler translates
    /// S3Server's <c>CompleteMultipartUpload</c> into this type so the service never depends on the S3
    /// library. The listed parts must be strictly ascending with no duplicates.
    /// </summary>
    public class CompleteMultipartUploadRequest
    {
        #region Public-Members

        /// <summary>
        /// The parts the client claims to have uploaded, in the order given. Never null.
        /// </summary>
        public List<CompletedPart> Parts
        {
            get
            {
                return _Parts;
            }
            set
            {
                _Parts = value ?? new List<CompletedPart>();
            }
        }

        #endregion

        #region Private-Members

        private List<CompletedPart> _Parts = new List<CompletedPart>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty complete-multipart-upload request.
        /// </summary>
        public CompleteMultipartUploadRequest()
        {
        }

        #endregion
    }
}
