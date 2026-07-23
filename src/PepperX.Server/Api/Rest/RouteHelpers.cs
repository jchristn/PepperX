namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Web;
    using PepperX.Core;
    using PepperX.Core.Enums;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Responses;
    using PepperX.Core.Serialization;
    using WatsonWebserver.Core;
    using ApiErrorResponse = PepperX.Core.Responses.ApiErrorResponse;

    /// <summary>
    /// Shared helpers for REST route handlers: uniform exception mapping and query/header parsing.
    /// </summary>
    public static class RouteHelpers
    {
        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a handler body, mapping domain and argument exceptions to error responses with the correct
        /// status code.
        /// </summary>
        /// <param name="request">API request.</param>
        /// <param name="body">Handler body.</param>
        /// <returns>The handler result or an error response.</returns>
        public static async Task<object> HandleAsync(ApiRequest request, Func<Task<object>> body)
        {
            try
            {
                return await body().ConfigureAwait(false);
            }
            catch (PepperXException ex)
            {
                request.Http.Response.StatusCode = ex.StatusCode;
                return new ApiErrorResponse(ex.ErrorType, ex.Message, ex.StatusCode);
            }
            catch (ArgumentException ex)
            {
                request.Http.Response.StatusCode = 400;
                return new ApiErrorResponse(ApiErrorEnum.BadRequest, ex.Message, 400);
            }
            catch (OperationCanceledException)
            {
                request.Http.Response.StatusCode = 499;
                return new ApiErrorResponse(ApiErrorEnum.BadRequest, "The request was cancelled.", 499);
            }
            catch (Exception ex)
            {
                request.Http.Response.StatusCode = 500;
                return new ApiErrorResponse(ApiErrorEnum.InternalError, ex.Message, 500);
            }
        }

        /// <summary>
        /// Get a required route parameter (the container name).
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>The container name.</returns>
        /// <exception cref="ArgumentException">The parameter is missing.</exception>
        public static string Container(ApiRequest request)
        {
            string? value = request.Parameters["container"];
            if (String.IsNullOrEmpty(value)) throw new ArgumentException("Container name is required.");
            return value;
        }

        /// <summary>
        /// Get the required object key from the <c>key</c> query parameter.
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>The decoded key.</returns>
        /// <exception cref="ArgumentException">The key query parameter is missing.</exception>
        public static string Key(ApiRequest request)
        {
            string? value = request.Query["key"];
            if (String.IsNullOrEmpty(value)) throw new ArgumentException("The 'key' query parameter is required.");
            return HttpUtility.UrlDecode(value);
        }

        /// <summary>
        /// Read an optional boolean query parameter.
        /// </summary>
        /// <param name="request">API request.</param>
        /// <param name="name">Parameter name.</param>
        /// <returns>True when the parameter is present and truthy.</returns>
        public static bool BoolQuery(ApiRequest request, string name)
        {
            string? value = request.Query[name];
            if (String.IsNullOrEmpty(value)) return false;
            return value == "1" || String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Read an optional string query parameter.
        /// </summary>
        /// <param name="request">API request.</param>
        /// <param name="name">Parameter name.</param>
        /// <returns>The value, or null.</returns>
        public static string? OptionalQuery(ApiRequest request, string name)
        {
            string? value = request.Query[name];
            return String.IsNullOrEmpty(value) ? null : HttpUtility.UrlDecode(value);
        }

        /// <summary>
        /// Convert the request's query parameters into a name/value collection for enumeration binding.
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>A name/value collection of query parameters.</returns>
        public static System.Collections.Specialized.NameValueCollection QueryToNvc(ApiRequest request)
        {
            System.Collections.Specialized.NameValueCollection nvc = new System.Collections.Specialized.NameValueCollection();
            foreach (string key in request.Query.GetKeys())
            {
                if (key != null) nvc[key] = HttpUtility.UrlDecode(request.Query[key]);
            }
            return nvc;
        }

        /// <summary>
        /// Parse the labels header (comma-separated).
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>The labels, or null when absent.</returns>
        public static List<string>? ParseLabelsHeader(ApiRequest request)
        {
            string? value = request.Headers[Constants.LabelsHeader];
            if (String.IsNullOrEmpty(value)) return null;

            List<string> labels = new List<string>();
            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) labels.Add(trimmed);
            }
            return labels;
        }

        /// <summary>
        /// Parse the tags header (URL-encoded <c>k=v&amp;k2=v2</c>).
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>The tags, or null when absent.</returns>
        public static Dictionary<string, string>? ParseTagsHeader(ApiRequest request)
        {
            string? value = request.Headers[Constants.TagsHeader];
            if (String.IsNullOrEmpty(value)) return null;

            Dictionary<string, string> tags = new Dictionary<string, string>();
            foreach (string pair in value.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string k = HttpUtility.UrlDecode(pair.Substring(0, eq));
                string v = HttpUtility.UrlDecode(pair.Substring(eq + 1));
                tags[k] = v;
            }
            return tags;
        }

        /// <summary>
        /// Parse the metadata-object header (base64-encoded JSON).
        /// </summary>
        /// <param name="request">API request.</param>
        /// <returns>The parsed object, or null when absent.</returns>
        public static object? ParseObjectHeader(ApiRequest request)
        {
            string? value = request.Headers[Constants.ObjectHeader];
            if (String.IsNullOrEmpty(value)) return null;

            byte[] bytes = Convert.FromBase64String(value);
            return _Serializer.DeserializeJson<object>(System.Text.Encoding.UTF8.GetString(bytes));
        }

        #endregion
    }
}
