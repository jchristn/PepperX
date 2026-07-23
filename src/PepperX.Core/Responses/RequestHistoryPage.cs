namespace PepperX.Core.Responses
{
    using System.Collections.Generic;
    using PepperX.Core.Models;

    /// <summary>
    /// A page of request history entries. List results omit request and response bodies to keep the payload
    /// small; fetch a single entry to retrieve its bodies.
    /// </summary>
    public class RequestHistoryPage
    {
        #region Public-Members

        /// <summary>
        /// Entries in this page. Never null.
        /// </summary>
        public List<RequestHistoryEntry> Entries
        {
            get
            {
                return _Entries;
            }
            set
            {
                _Entries = value ?? new List<RequestHistoryEntry>();
            }
        }

        /// <summary>
        /// One-based page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Total number of entries matching the filter across all pages.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        #endregion

        #region Private-Members

        private List<RequestHistoryEntry> _Entries = new List<RequestHistoryEntry>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty request history page.
        /// </summary>
        public RequestHistoryPage()
        {
        }

        #endregion
    }
}
