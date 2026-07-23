namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Enums;
    using PepperX.Core.Helpers;
    using PepperX.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Helpers for database-backed Touchstone cases. Cases are skipped when the test database is unavailable.
    /// </summary>
    public static class DbTest
    {
        /// <summary>
        /// Build a database-backed test case that receives the shared driver, or a skipped case when the
        /// database is unavailable.
        /// </summary>
        /// <param name="suiteId">Suite identifier.</param>
        /// <param name="caseId">Case identifier.</param>
        /// <param name="displayName">Display name.</param>
        /// <param name="body">Case body receiving the driver and a token.</param>
        /// <returns>A descriptor.</returns>
        public static TestCaseDescriptor Case(string suiteId, string caseId, string displayName, Func<IMetadataDatabaseDriver, CancellationToken, Task> body)
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestCaseDescriptor(suiteId, caseId, displayName, _ => Task.CompletedTask,
                    skip: true, skipReason: "PostgreSQL test database unavailable");
            }

            return new TestCaseDescriptor(suiteId, caseId, displayName, async ct =>
            {
                IMetadataDatabaseDriver driver = await PostgresTestFixture.GetSharedAsync(ct).ConfigureAwait(false);
                await body(driver, ct).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Generate a unique, valid container name.
        /// </summary>
        /// <returns>Container name.</returns>
        public static string NewContainerName()
        {
            return "c" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        /// <summary>
        /// Create and persist a container with a unique name.
        /// </summary>
        /// <param name="driver">Driver.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created container.</returns>
        public static async Task<Container> NewContainerAsync(IMetadataDatabaseDriver driver, CancellationToken token)
        {
            Container container = new Container { Name = NewContainerName() };
            return await driver.Containers.CreateAsync(container, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build an in-memory extent for a container and key with an arbitrary storage location.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="key">Object key.</param>
        /// <param name="sizeBytes">Payload size.</param>
        /// <param name="labels">Optional labels.</param>
        /// <param name="tags">Optional tags.</param>
        /// <returns>An extent (not yet persisted).</returns>
        public static Extent MakeExtent(string containerId, string key, long sizeBytes, List<string>? labels = null, Dictionary<string, string>? tags = null)
        {
            Extent extent = new Extent
            {
                ContainerId = containerId,
                Key = key,
                SizeBytes = sizeBytes,
                Sha256 = new string('0', 64),
                StorageDriver = StorageDriverTypeEnum.Disk,
                StorageLocation = containerId + "/xx/" + IdGenerator.GenerateExtentId() + ".pxe",
                HasMetadataObject = false
            };
            if (labels != null) extent.Labels = labels;
            if (tags != null) extent.Tags = tags;
            return extent;
        }
    }
}
