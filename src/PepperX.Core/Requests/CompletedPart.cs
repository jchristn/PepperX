namespace PepperX.Core.Requests
{
    using System;

    /// <summary>
    /// A single entry in a complete-multipart-upload request: the part number the client uploaded and the
    /// ETag (part MD5) it received. The service validates each entry against the staged part before
    /// assembling the object.
    /// </summary>
    public class CompletedPart
    {
        #region Public-Members

        /// <summary>
        /// Part number. Clamped to the range 1 to 10000.
        /// </summary>
        public int PartNumber
        {
            get
            {
                return _PartNumber;
            }
            set
            {
                _PartNumber = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// ETag the client received for the part (its MD5), with any surrounding quotes stripped.
        /// </summary>
        public string ETag
        {
            get
            {
                return _ETag;
            }
            set
            {
                _ETag = value == null ? String.Empty : value.Trim().Trim('"');
            }
        }

        #endregion

        #region Private-Members

        private int _PartNumber = 1;
        private string _ETag = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty completed-part entry.
        /// </summary>
        public CompletedPart()
        {
        }

        /// <summary>
        /// Instantiate a completed-part entry.
        /// </summary>
        /// <param name="partNumber">Part number (1 to 10000).</param>
        /// <param name="etag">Part ETag (MD5).</param>
        public CompletedPart(int partNumber, string etag)
        {
            PartNumber = partNumber;
            ETag = etag;
        }

        #endregion
    }
}
