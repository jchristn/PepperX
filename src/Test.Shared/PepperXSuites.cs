namespace Test.Shared
{
    using System.Collections.Generic;
    using Test.Shared.Suites;
    using Touchstone.Core;

    /// <summary>
    /// Central registry of PepperX test suite descriptors. Runners consume <see cref="All"/>.
    /// Suites are added phase by phase; see PEPPERX_PLAN.md Appendix A for the target set.
    /// </summary>
    public static class PepperXSuites
    {
        /// <summary>
        /// All registered test suites, in execution order.
        /// </summary>
        public static IReadOnlyList<TestSuiteDescriptor> All
        {
            get
            {
                return new List<TestSuiteDescriptor>
                {
                    IdentifierSuite.Build(),
                    ModelValidationSuite.Build(),
                    EnumerationQuerySuite.Build(),
                    SerializationSuite.Build(),
                    SettingsSuite.Build(),
                    ExtentFormatSuite.Build(),
                    DiskStorageDriverSuite.Build(),
                    ContainerCacheSettingsValidationSuite.Build(),
                    DatabaseMigrationSuite.Build(),
                    DatabaseContainerSuite.Build(),
                    DatabaseContainerCacheSuite.Build(),
                    DatabaseExtentSuite.Build(),
                    DatabaseLeaseSuite.Build(),
                    DatabaseEnumerationSuite.Build(),
                    DatabaseRequestHistorySuite.Build(),
                    ObjectLifecycleSuite.Build(),
                    ContainerCacheSuite.Build(),
                    ContainerCacheConcurrencySuite.Build(),
                    ContainerCacheConsistencySuite.Build(),
                    ContainerCacheSoakSuite.Build(),
                    MultiNodeSemanticsSuite.Build(),
                    RehydrationSuite.Build(),
                    RestApiSuite.Build(),
                    S3ProtocolSuite.Build(),
                    RespInteropSuite.Build(),
                    WebsocketProtocolSuite.Build(),
                    McpProtocolSuite.Build(),
                    ProtocolParitySuite.Build(),
                    RobustnessSuite.Build()
                };
            }
        }
    }
}
