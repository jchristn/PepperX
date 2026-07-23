namespace PepperX.Server.Api.Rest
{
    using System;
    using System.Threading.Tasks;
    using PepperX.Core;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Health and liveness routes.
    /// </summary>
    public sealed class HealthRoutes
    {
        #region Private-Members

        private readonly DateTime _StartUtc;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate health routes.
        /// </summary>
        /// <param name="startUtc">Server start time.</param>
        public HealthRoutes(DateTime startUtc)
        {
            _StartUtc = startUtc;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the health routes.
        /// </summary>
        /// <param name="server">Web server.</param>
        /// <exception cref="ArgumentNullException"><paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Get("/", GetRootAsync, openApi => openApi
                .WithTag("Health")
                .WithDescription("Returns product name, version, and uptime in milliseconds.")
                .WithResponse(200, OpenApiResponseMetadata.Create("Server health information")));

            server.Head("/", HeadRootAsync, openApi => openApi
                .WithTag("Health")
                .WithDescription("Returns 200 when the server is running.")
                .WithResponse(200, OpenApiResponseMetadata.Create("Server is running")));

            server.Get("/v1.0/api/health", GetHealthAsync, openApi => openApi
                .WithTag("Health")
                .WithDescription("Health check for dashboards and monitoring.")
                .WithResponse(200, OpenApiResponseMetadata.Create("Healthy")));
        }

        #endregion

        #region Private-Methods

        private Task<object> GetRootAsync(ApiRequest request)
        {
            object body = new
            {
                Name = Constants.ProductName,
                Version = Constants.ProductVersion,
                StartTimeUtc = _StartUtc,
                UptimeMs = (DateTime.UtcNow - _StartUtc).TotalMilliseconds
            };
            return Task.FromResult(body);
        }

        private Task<object> HeadRootAsync(ApiRequest request)
        {
            request.Http.Response.StatusCode = 200;
            return Task.FromResult<object>(null!);
        }

        private Task<object> GetHealthAsync(ApiRequest request)
        {
            object body = new { Status = "Healthy", Product = Constants.ProductName, Version = Constants.ProductVersion };
            return Task.FromResult(body);
        }

        #endregion
    }
}
