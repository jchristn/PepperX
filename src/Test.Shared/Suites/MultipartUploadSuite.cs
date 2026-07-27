namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using PepperX.Core.Exceptions;
    using PepperX.Core.Models;
    using PepperX.Core.Requests;
    using PepperX.Core.Responses;
    using PepperX.Core.Services;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the multipart upload lifecycle through the service layer: initiate, upload part (including
    /// copy), complete (assembly + ETag), abort, expiry, validation, pagination, and caching interaction.
    /// </summary>
    public static class MultipartUploadSuite
    {
        /// <summary>
        /// Build the multipart upload suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "MultipartUpload",
                displayName: "Multipart upload (service)",
                cases: new List<TestCaseDescriptor>
                {
                    DbTest.StackCase("MultipartUpload", "HappyPath", "Initiate, upload parts, complete, and read the assembled object", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "big/object.bin", "application/octet-stream", null, ct);

                        byte[] p1 = Bytes("part-one-");
                        byte[] p2 = Bytes("part-two-");
                        byte[] p3 = Bytes("part-three");
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, p1, ct);
                        MultipartPart r2 = await StageAsync(stack, container, upload.Id, 2, p2, ct);
                        MultipartPart r3 = await StageAsync(stack, container, upload.Id, 3, p3, ct);

                        CompleteMultipartUploadRequest request = Request((1, r1.Md5), (2, r2.Md5), (3, r3.Md5));
                        CompleteMultipartUploadResponse done = await stack.Multipart.CompleteAsync(container, upload.Id, request, ct);

                        byte[] expected = Concat(p1, p2, p3);
                        byte[] actual = await ReadObjectAsync(stack, container, "big/object.bin", ct);
                        Check.Equal(Convert.ToHexString(expected), Convert.ToHexString(actual), "assembled bytes");
                        Check.Equal(expected.Length, (int)done.SizeBytes, "assembled size");

                        // Staged parts are reclaimed and the upload row is gone.
                        Check.True((await stack.Db.MultipartUploads.ReadUploadAsync(upload.Id, ct)) == null, "upload row gone");
                    }),

                    DbTest.StackCase("MultipartUpload", "EtagFormula", "Part ETag is MD5; complete ETag is md5-of-md5s-N and is stored", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);

                        byte[] p1 = Bytes("alpha");
                        byte[] p2 = Bytes("bravo");
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, p1, ct);
                        MultipartPart r2 = await StageAsync(stack, container, upload.Id, 2, p2, ct);
                        Check.Equal(Md5Hex(p1), r1.Md5, "part1 md5");
                        Check.Equal(Md5Hex(p2), r2.Md5, "part2 md5");

                        CompleteMultipartUploadResponse done = await stack.Multipart.CompleteAsync(container, upload.Id, Request((1, r1.Md5), (2, r2.Md5)), ct);
                        string expectedEtag = ExpectedMultipartEtag(r1.Md5, r2.Md5);
                        Check.Equal(expectedEtag, done.ETag, "multipart etag formula");

                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(container, "k", ct);
                        Check.NotNull(meta, "metadata");
                        Check.Equal(expectedEtag, meta!.Etag ?? "", "stored etag on object");
                    }),

                    DbTest.StackCase("MultipartUpload", "ValidationOrder", "Non-ascending parts are rejected", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, Bytes("a"), ct);
                        MultipartPart r2 = await StageAsync(stack, container, upload.Id, 2, Bytes("b"), ct);

                        await Check.ThrowsAsync<InvalidPartOrderException>(
                            () => stack.Multipart.CompleteAsync(container, upload.Id, Request((2, r2.Md5), (1, r1.Md5)), ct), "descending rejected");
                    }),

                    DbTest.StackCase("MultipartUpload", "ValidationEtag", "A mismatched part ETag is rejected", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        await StageAsync(stack, container, upload.Id, 1, Bytes("a"), ct);

                        await Check.ThrowsAsync<InvalidPartException>(
                            () => stack.Multipart.CompleteAsync(container, upload.Id, Request((1, Md5Hex(Bytes("wrong")))), ct), "bad etag rejected");
                    }),

                    DbTest.StackCase("MultipartUpload", "ValidationMinSize", "A non-final part below the minimum is rejected; the last may be smaller", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 16;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, Bytes("tiny"), ct);   // 4 bytes < 16, non-last
                        MultipartPart r2 = await StageAsync(stack, container, upload.Id, 2, Bytes("also-small"), ct);

                        await Check.ThrowsAsync<EntityTooSmallException>(
                            () => stack.Multipart.CompleteAsync(container, upload.Id, Request((1, r1.Md5), (2, r2.Md5)), ct), "small non-final part rejected");

                        // A single (last) small part is allowed.
                        MultipartUpload upload2 = await stack.Multipart.InitiateAsync(container, "k2", null, null, ct);
                        MultipartPart s1 = await StageAsync(stack, container, upload2.Id, 1, Bytes("small"), ct);
                        await stack.Multipart.CompleteAsync(container, upload2.Id, Request((1, s1.Md5)), ct);
                        Check.Equal("small", Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "k2", ct)), "single small part ok");
                    }),

                    DbTest.StackCase("MultipartUpload", "Abort", "Abort discards parts and a later complete fails", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, Bytes("data"), ct);

                        await stack.Multipart.AbortAsync(container, upload.Id, ct);
                        Check.True((await stack.Db.MultipartUploads.ReadUploadAsync(upload.Id, ct)) == null, "upload gone");
                        await Check.ThrowsAsync<NoSuchUploadException>(
                            () => stack.Multipart.CompleteAsync(container, upload.Id, Request((1, r1.Md5)), ct), "complete after abort fails");
                    }),

                    DbTest.StackCase("MultipartUpload", "Expiry", "The janitor reclaims an expired upload", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        string containerId = (await stack.Db.Containers.ReadByNameAsync(container, ct))!.Id;

                        MultipartUpload upload = new MultipartUpload
                        {
                            ContainerId = containerId,
                            Key = "k",
                            InitiatedUtc = DateTime.UtcNow.AddDays(-10),
                            ExpiresUtc = DateTime.UtcNow.AddDays(-1)
                        };
                        await stack.Db.MultipartUploads.CreateUploadAsync(upload, ct);
                        await StageAsync(stack, container, upload.Id, 1, Bytes("data"), ct);

                        await stack.Janitor.RunOnceAsync(ct);
                        Check.True((await stack.Db.MultipartUploads.ReadUploadAsync(upload.Id, ct)) == null, "expired upload purged");
                    }),

                    DbTest.StackCase("MultipartUpload", "UploadPartCopy", "A part can be copied from an existing object, full and ranged", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        await WriteObjectAsync(stack, container, "source", Bytes("0123456789"), ct);

                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "dest", null, null, ct);
                        MultipartPart inline = await StageAsync(stack, container, upload.Id, 1, Bytes("HEAD-"), ct);
                        MultipartPart copyFull = await stack.Multipart.UploadPartCopyAsync(container, upload.Id, 2, container, "source", null, null, ct);
                        MultipartPart copyRange = await stack.Multipart.UploadPartCopyAsync(container, upload.Id, 3, container, "source", 2, 3, ct); // "234"

                        await stack.Multipart.CompleteAsync(container, upload.Id, Request((1, inline.Md5), (2, copyFull.Md5), (3, copyRange.Md5)), ct);
                        Check.Equal("HEAD-0123456789234", Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "dest", ct)), "copy assembly");

                        // A copy from a missing source fails (use a fresh upload, since the one above is completed).
                        MultipartUpload upload2 = await stack.Multipart.InitiateAsync(container, "dest2", null, null, ct);
                        await Check.ThrowsAsync<ObjectNotFoundException>(
                            () => stack.Multipart.UploadPartCopyAsync(container, upload2.Id, 1, container, "missing", null, null, ct), "copy from missing source");
                    }),

                    DbTest.StackCase("MultipartUpload", "ListPartsPagination", "ListParts paginates ascending with markers", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = await NewContainerAsync(stack, ct);
                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        for (int i = 1; i <= 5; i++) await StageAsync(stack, container, upload.Id, i, Bytes("p" + i), ct);

                        List<int> seen = new List<int>();
                        int marker = 0;
                        while (true)
                        {
                            MultipartPartListResult page = await stack.Multipart.ListPartsAsync(container, upload.Id, marker, 2, ct);
                            foreach (MultipartPart p in page.Parts) seen.Add(p.PartNumber);
                            if (!page.IsTruncated) break;
                            marker = page.NextPartNumberMarker!.Value;
                        }
                        Check.Equal("1,2,3,4,5", String.Join(",", seen), "all parts, ascending, no dupes");
                    }),

                    DbTest.StackCase("MultipartUpload", "ListUploadsPagination", "ListUploads paginates and is scoped to the container", async (stack, ct) =>
                    {
                        string container = await NewContainerAsync(stack, ct);
                        await stack.Multipart.InitiateAsync(container, "a", null, null, ct);
                        await stack.Multipart.InitiateAsync(container, "b", null, null, ct);
                        await stack.Multipart.InitiateAsync(container, "c", null, null, ct);

                        MultipartUploadListResult first = await stack.Multipart.ListUploadsAsync(container, null, null, 2, ct);
                        Check.Equal(2, first.Uploads.Count, "first page size");
                        Check.True(first.IsTruncated, "truncated");
                        MultipartUploadListResult second = await stack.Multipart.ListUploadsAsync(container, first.NextKeyMarker, first.NextUploadIdMarker, 2, ct);
                        Check.Equal(1, second.Uploads.Count, "second page size");
                        Check.False(second.IsTruncated, "not truncated");
                    }),

                    DbTest.StackCase("MultipartUpload", "CachingInteraction", "A completed multipart object works through a cache-enabled container", async (stack, ct) =>
                    {
                        stack.S3.MultipartMinPartBytes = 0;
                        string container = DbTest.NewContainerName();
                        await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = container, Cache = new UpdateCacheSettingsRequest { Enabled = true } }, ct);

                        MultipartUpload upload = await stack.Multipart.InitiateAsync(container, "k", null, null, ct);
                        MultipartPart r1 = await StageAsync(stack, container, upload.Id, 1, Bytes("cached-multipart-object"), ct);
                        CompleteMultipartUploadResponse done = await stack.Multipart.CompleteAsync(container, upload.Id, Request((1, r1.Md5)), ct);

                        // Read twice: first hydrates, second is a cache hit; both must return the bytes and consistent ETag.
                        Check.Equal("cached-multipart-object", Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "k", ct)), "first read");
                        Check.Equal("cached-multipart-object", Encoding.UTF8.GetString(await ReadObjectAsync(stack, container, "k", ct)), "second read (hit)");
                        ObjectMetadata? meta = await stack.Reads.ReadMetadataAsync(container, "k", ct);
                        Check.Equal(done.ETag, meta!.Etag ?? "", "etag consistent through cache");
                    })
                });
        }

        #region Private-Methods

        private static async Task<string> NewContainerAsync(ServiceStack stack, CancellationToken ct)
        {
            string name = DbTest.NewContainerName();
            await stack.Containers.CreateAsync(new ContainerCreateRequest { Name = name }, ct);
            return name;
        }

        private static async Task<MultipartPart> StageAsync(ServiceStack stack, string container, string uploadId, int partNumber, byte[] bytes, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                return await stack.Multipart.UploadPartAsync(container, uploadId, partNumber, ms, ct);
            }
        }

        private static async Task WriteObjectAsync(ServiceStack stack, string container, string key, byte[] bytes, CancellationToken ct)
        {
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                await stack.Writes.WriteAsync(container, key, ms, "application/octet-stream", null, null, null, false, ct);
            }
        }

        private static async Task<byte[]> ReadObjectAsync(ServiceStack stack, string container, string key, CancellationToken ct)
        {
            await using (ObjectReadHandle? handle = await stack.Reads.ReadAsync(container, key, null, null, ct))
            {
                Check.NotNull(handle, "read handle for " + key);
                using (MemoryStream ms = new MemoryStream())
                {
                    await handle!.Payload.CopyToAsync(ms, ct);
                    return ms.ToArray();
                }
            }
        }

        private static CompleteMultipartUploadRequest Request(params (int PartNumber, string ETag)[] parts)
        {
            CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest();
            foreach ((int PartNumber, string ETag) p in parts) request.Parts.Add(new CompletedPart(p.PartNumber, p.ETag));
            return request;
        }

        private static byte[] Bytes(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                foreach (byte[] a in arrays) ms.Write(a, 0, a.Length);
                return ms.ToArray();
            }
        }

        private static string Md5Hex(byte[] bytes)
        {
            return Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        }

        private static string ExpectedMultipartEtag(params string[] partMd5s)
        {
            byte[] concat = new byte[partMd5s.Length * 16];
            for (int i = 0; i < partMd5s.Length; i++) Array.Copy(Convert.FromHexString(partMd5s[i]), 0, concat, i * 16, 16);
            return Convert.ToHexString(MD5.HashData(concat)).ToLowerInvariant() + "-" + partMd5s.Length;
        }

        #endregion
    }
}
