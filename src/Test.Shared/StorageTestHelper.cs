namespace Test.Shared
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Helpers;
    using PepperX.Core.Settings;
    using PepperX.Core.Storage;
    using PepperX.Core.Storage.Disk;
    using PepperX.Core.Storage.Format;

    /// <summary>
    /// Helpers for exercising the extent storage layer against a throwaway temporary root.
    /// </summary>
    public static class StorageTestHelper
    {
        /// <summary>
        /// Create a unique temporary root directory path (not yet created).
        /// </summary>
        /// <returns>Root directory path.</returns>
        public static string NewRoot()
        {
            return Path.Combine(Path.GetTempPath(), "pepperx-store-" + Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Create and initialize a disk storage driver over a root directory.
        /// </summary>
        /// <param name="root">Root directory.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized driver.</returns>
        public static async Task<DiskExtentStorageDriver> NewDriverAsync(string root, CancellationToken token = default)
        {
            DiskStorageSettings settings = new DiskStorageSettings { RootDirectory = root };
            DiskExtentStorageDriver driver = new DiskExtentStorageDriver(settings);
            await driver.InitializeAsync(token).ConfigureAwait(false);
            return driver;
        }

        /// <summary>
        /// Build an extent header for a container and key.
        /// </summary>
        /// <param name="containerId">Container identifier.</param>
        /// <param name="containerName">Container name.</param>
        /// <param name="key">Object key.</param>
        /// <returns>A header with a generated extent identifier.</returns>
        public static ExtentHeader NewHeader(string containerId, string containerName, string key)
        {
            return new ExtentHeader
            {
                ExtentId = IdGenerator.GenerateExtentId(),
                ContainerId = containerId,
                ContainerName = containerName,
                Key = key,
                CreatedUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Write a byte payload for a header and return the result.
        /// </summary>
        /// <param name="driver">Storage driver.</param>
        /// <param name="header">Header to persist.</param>
        /// <param name="payload">Payload bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The write result.</returns>
        public static async Task<ExtentWriteResult> WriteBytesAsync(IExtentStorageDriver driver, ExtentHeader header, byte[] payload, CancellationToken token = default)
        {
            using (MemoryStream ms = new MemoryStream(payload))
            {
                return await driver.WriteAsync(header, ms, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Read the full payload of a stored extent into a byte array.
        /// </summary>
        /// <param name="driver">Storage driver.</param>
        /// <param name="location">Extent location.</param>
        /// <param name="verifyChecksum">Whether to verify the checksum on read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The payload bytes.</returns>
        public static async Task<byte[]> ReadAllAsync(IExtentStorageDriver driver, string location, bool verifyChecksum = false, CancellationToken token = default)
        {
            using (ExtentPayloadStream payload = await driver.OpenReadAsync(location, verifyChecksum, token).ConfigureAwait(false))
            using (MemoryStream ms = new MemoryStream())
            {
                await payload.CopyToAsync(ms, token).ConfigureAwait(false);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Delete a temporary root directory, ignoring errors.
        /// </summary>
        /// <param name="root">Root directory.</param>
        public static void Cleanup(string root)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
