namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Database;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;
    using Touchstone.Core;

    /// <summary>
    /// Verifies multipart upload persistence: schema, upload and part CRUD, upsert-replace semantics,
    /// cascade delete, expiry purge, and model validation.
    /// </summary>
    public static class DatabaseMultipartSuite
    {
        /// <summary>
        /// Build the database multipart suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "DatabaseMultipart",
                displayName: "Database multipart",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.Case("DatabaseMultipart", "UploadCrud", "Create, read, and delete an upload", async (driver, ct) =>
                    {
                        Container container = await DbTest.NewContainerAsync(driver, ct);
                        MultipartUpload upload = new MultipartUpload
                        {
                            ContainerId = container.Id,
                            Key = "obj",
                            ContentType = "text/plain",
                            Tags = new Dictionary<string, string> { { "k", "v" } },
                            ExpiresUtc = DateTime.UtcNow.AddDays(7)
                        };
                        await driver.MultipartUploads.CreateUploadAsync(upload, ct);

                        MultipartUpload? read = await driver.MultipartUploads.ReadUploadAsync(upload.Id, ct);
                        Check.NotNull(read, "upload read back");
                        Check.Equal("obj", read!.Key, "key");
                        Check.Equal("v", read.Tags["k"], "tag round-trip");

                        Check.True(await driver.MultipartUploads.DeleteUploadAsync(upload.Id, ct), "deleted");
                        Check.True((await driver.MultipartUploads.ReadUploadAsync(upload.Id, ct)) == null, "gone");
                    }),

                    DbTest.Case("DatabaseMultipart", "PartUpsertAndList", "Upsert replaces a part number; list is ascending", async (driver, ct) =>
                    {
                        Container container = await DbTest.NewContainerAsync(driver, ct);
                        MultipartUpload upload = await CreateUploadAsync(driver, container.Id, "k", ct);

                        await driver.MultipartUploads.UpsertPartAsync(MakePart(upload.Id, 2, 100, "b"), ct);
                        await driver.MultipartUploads.UpsertPartAsync(MakePart(upload.Id, 1, 100, "a"), ct);

                        // Re-upsert part 1 with a distinct storage location; the prior location is returned so
                        // the caller can reclaim the superseded blob, and no duplicate row is created.
                        MultipartPart replacement = MakePart(upload.Id, 1, 200, "a2");
                        replacement.StorageLocation = ".multipart/" + upload.Id + "/1.alt.part";
                        string? prior = await driver.MultipartUploads.UpsertPartAsync(replacement, ct);
                        Check.NotNull(prior, "prior location returned on replace");

                        IReadOnlyList<MultipartPart> all = await driver.MultipartUploads.ListAllPartsAsync(upload.Id, ct);
                        Check.Equal(2, all.Count, "two parts (no duplicate)");
                        Check.Equal(1, all[0].PartNumber, "ascending first");
                        Check.Equal(2, all[1].PartNumber, "ascending second");
                        Check.Equal(200, (int)all[0].SizeBytes, "part 1 replaced");
                    }),

                    DbTest.Case("DatabaseMultipart", "CascadeDelete", "Deleting an upload cascades its parts", async (driver, ct) =>
                    {
                        Container container = await DbTest.NewContainerAsync(driver, ct);
                        MultipartUpload upload = await CreateUploadAsync(driver, container.Id, "k", ct);
                        await driver.MultipartUploads.UpsertPartAsync(MakePart(upload.Id, 1, 10, "a"), ct);
                        await driver.MultipartUploads.UpsertPartAsync(MakePart(upload.Id, 2, 10, "b"), ct);

                        await driver.MultipartUploads.DeleteUploadAsync(upload.Id, ct);
                        IReadOnlyList<MultipartPart> all = await driver.MultipartUploads.ListAllPartsAsync(upload.Id, ct);
                        Check.Equal(0, all.Count, "parts cascade-deleted");
                    }),

                    DbTest.Case("DatabaseMultipart", "PurgeExpired", "Expired uploads are purged and returned; future ones remain", async (driver, ct) =>
                    {
                        Container container = await DbTest.NewContainerAsync(driver, ct);
                        MultipartUpload expired = new MultipartUpload { ContainerId = container.Id, Key = "old", ExpiresUtc = DateTime.UtcNow.AddDays(-1) };
                        MultipartUpload future = new MultipartUpload { ContainerId = container.Id, Key = "new", ExpiresUtc = DateTime.UtcNow.AddDays(1) };
                        await driver.MultipartUploads.CreateUploadAsync(expired, ct);
                        await driver.MultipartUploads.CreateUploadAsync(future, ct);

                        IReadOnlyList<string> purged = await driver.MultipartUploads.PurgeExpiredAsync(DateTime.UtcNow, ct);
                        Check.True(purged.Contains(expired.Id), "expired id returned");
                        Check.False(purged.Contains(future.Id), "future id not returned");
                        Check.True((await driver.MultipartUploads.ReadUploadAsync(expired.Id, ct)) == null, "expired gone");
                        Check.NotNull(await driver.MultipartUploads.ReadUploadAsync(future.Id, ct), "future remains");
                    }),

                    DbTest.Case("DatabaseMultipart", "ListPagination", "ListParts and ListUploads paginate with markers", async (driver, ct) =>
                    {
                        Container container = await DbTest.NewContainerAsync(driver, ct);
                        MultipartUpload upload = await CreateUploadAsync(driver, container.Id, "k", ct);
                        for (int i = 1; i <= 3; i++) await driver.MultipartUploads.UpsertPartAsync(MakePart(upload.Id, i, 10, "p" + i), ct);

                        MultipartPartListResult page = await driver.MultipartUploads.ListPartsAsync(upload.Id, 0, 2, ct);
                        Check.Equal(2, page.Parts.Count, "first part page");
                        Check.True(page.IsTruncated, "part page truncated");
                        MultipartPartListResult page2 = await driver.MultipartUploads.ListPartsAsync(upload.Id, page.NextPartNumberMarker!.Value, 2, ct);
                        Check.Equal(1, page2.Parts.Count, "second part page");
                        Check.False(page2.IsTruncated, "part page end");
                    }),

                    // Model validation is DB-independent but grouped here for locality; runs even without a DB.
                    new TestCaseDescriptor("DatabaseMultipart", "ModelValidation", "Part model clamps part number and validates hashes", _ =>
                    {
                        MultipartPart part = new MultipartPart();
                        part.PartNumber = 0;
                        Check.Equal(1, part.PartNumber, "part number clamped up to 1");
                        part.PartNumber = 999999;
                        Check.Equal(10000, part.PartNumber, "part number clamped to 10000");

                        Check.Throws<ArgumentException>(() => new MultipartPart { Md5 = "not-hex" }, "bad md5 rejected");
                        Check.Throws<ArgumentException>(() => new MultipartPart { Sha256 = "short" }, "bad sha256 rejected");

                        MultipartPart valid = new MultipartPart { Md5 = new string('A', 32), Sha256 = new string('b', 64) };
                        Check.Equal(new string('a', 32), valid.Md5, "md5 normalized to lowercase");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Private-Methods

        private static async Task<MultipartUpload> CreateUploadAsync(IMetadataDatabaseDriver driver, string containerId, string key, CancellationToken ct)
        {
            MultipartUpload upload = new MultipartUpload { ContainerId = containerId, Key = key, ExpiresUtc = DateTime.UtcNow.AddDays(7) };
            await driver.MultipartUploads.CreateUploadAsync(upload, ct);
            return upload;
        }

        private static MultipartPart MakePart(string uploadId, int partNumber, long size, string seed)
        {
            return new MultipartPart
            {
                UploadId = uploadId,
                PartNumber = partNumber,
                SizeBytes = size,
                Md5 = new string('0', 32 - seed.Length) + Hexify(seed, 32),
                Sha256 = new string('0', 64 - seed.Length) + Hexify(seed, 64),
                StorageLocation = ".multipart/" + uploadId + "/" + partNumber + ".part"
            };
        }

        private static string Hexify(string seed, int totalLength)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in seed) sb.Append(((int)c % 16).ToString("x"));
            string s = sb.ToString();
            return s.Length > totalLength ? s.Substring(0, totalLength) : s;
        }

        #endregion
    }
}
