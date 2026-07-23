namespace PepperX.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using PepperX.Core.Settings;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Captures a durable record of each REST request from the Watson PostRouting hook. Entry construction is
    /// synchronous so request state is not garbage-collected before the write; the database insert is
    /// dispatched fire-and-forget so it never blocks the response. Secrets are redacted and bodies are
    /// truncated; configured data-plane paths are captured without bodies.
    /// </summary>
    public sealed class RequestHistoryCaptureService
    {
        #region Private-Members

        private readonly string _Header = "[RequestHistoryCaptureService] ";
        private readonly IMetadataDatabaseDriver _Db;
        private readonly RequestHistorySettings _Settings;
        private readonly LoggingModule? _Logging;
        private readonly HashSet<string> _RedactExact;
        private readonly List<Regex> _ExcludeBodyPatterns;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the capture service.
        /// </summary>
        /// <param name="db">Metadata database driver.</param>
        /// <param name="settings">Request history settings.</param>
        /// <param name="logging">Optional logging module.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public RequestHistoryCaptureService(IMetadataDatabaseDriver db, RequestHistorySettings settings, LoggingModule? logging = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging;

            _RedactExact = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "authorization", "proxy-authorization", "cookie", "set-cookie"
            };

            _ExcludeBodyPatterns = new List<Regex>();
            foreach (string glob in _Settings.ExcludeBodyPaths) _ExcludeBodyPatterns.Add(GlobToRegex(glob));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Capture a completed request. Safe to call from the PostRouting hook.
        /// </summary>
        /// <param name="context">The HTTP context of the completed request.</param>
        public void Capture(HttpContextBase context)
        {
            if (context == null) return;

            try
            {
                RequestHistoryEntry entry = Build(context);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _Db.RequestHistory.CreateAsync(entry, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _Logging?.Debug(_Header + "failed to persist request history: " + ex.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                _Logging?.Debug(_Header + "failed to build request history entry: " + ex.Message);
            }
        }

        #endregion

        #region Private-Methods

        private RequestHistoryEntry Build(HttpContextBase context)
        {
            string path = context.Request.Url.RawWithoutQuery ?? String.Empty;
            bool excludeBody = IsExcluded(path);

            RequestHistoryEntry entry = new RequestHistoryEntry
            {
                Method = context.Request.Method.ToString(),
                Path = path,
                Url = context.Request.Url.RawWithQuery ?? path,
                StatusCode = context.Response.StatusCode,
                DurationMs = context.Timestamp.TotalMs ?? 0,
                SourceIp = context.Request.Source?.IpAddress,
                CreatedUtc = context.Timestamp.Start,
                CompletedUtc = context.Timestamp.End,
                RequestHeaders = RedactHeaders(context.Request.Headers)
            };

            if (!excludeBody)
            {
                string? requestBody = SafeRequestBody(context);
                ApplyBody(requestBody, _Settings.MaxRequestBodyBytes,
                    (body, bytes, truncated) => { entry.RequestBody = body; entry.RequestBodyBytes = bytes; entry.RequestBodyTruncated = truncated; });

                string? responseBody = SafeResponseBody(context);
                ApplyBody(responseBody, _Settings.MaxResponseBodyBytes,
                    (body, bytes, truncated) => { entry.ResponseBody = body; entry.ResponseBodyBytes = bytes; entry.ResponseBodyTruncated = truncated; });
            }

            return entry;
        }

        private Dictionary<string, string> RedactHeaders(System.Collections.Specialized.NameValueCollection? headers)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (headers == null) return result;

            foreach (string? key in headers.AllKeys)
            {
                if (key == null) continue;
                result[key] = IsSensitive(key) ? "[redacted]" : (headers[key] ?? String.Empty);
            }

            return result;
        }

        private bool IsSensitive(string headerName)
        {
            if (_RedactExact.Contains(headerName)) return true;
            string lower = headerName.ToLowerInvariant();
            return lower.Contains("api-key") || lower.Contains("token");
        }

        private bool IsExcluded(string path)
        {
            foreach (Regex pattern in _ExcludeBodyPatterns)
            {
                if (pattern.IsMatch(path)) return true;
            }
            return false;
        }

        private static void ApplyBody(string? body, int maxBytes, Action<string?, long, bool> assign)
        {
            if (String.IsNullOrEmpty(body))
            {
                assign(null, 0, false);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            if (bytes.Length <= maxBytes)
            {
                assign(body, bytes.Length, false);
                return;
            }

            string truncated = Encoding.UTF8.GetString(bytes, 0, maxBytes);
            assign(truncated, bytes.Length, true);
        }

        private static string? SafeRequestBody(HttpContextBase context)
        {
            try
            {
                return context.Request.DataAsString;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? SafeResponseBody(HttpContextBase context)
        {
            try
            {
                return context.Response.DataAsString;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Regex GlobToRegex(string glob)
        {
            StringBuilder sb = new StringBuilder("^");
            foreach (char c in glob)
            {
                if (c == '*') sb.Append("[^?]*");
                else sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        #endregion
    }
}
