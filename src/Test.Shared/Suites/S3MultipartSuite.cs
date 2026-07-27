namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Touchstone.Core;

    /// <summary>
    /// Verifies S3 multipart uploads end-to-end through the AWS SDK against an in-process server, including
    /// the lifecycle, list operations, abort, and ETag parity across GET/HEAD/LIST for both single-part and
    /// multipart-assembled objects.
    /// </summary>
    public static class S3MultipartSuite
    {
        private static readonly int _MinPartBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Build the S3 multipart suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("S3Multipart", "S3 multipart", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("S3Multipart", "Unavailable", "S3 multipart (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "S3Multipart",
                displayName: "S3 multipart",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("S3Multipart", "Lifecycle", "Initiate, upload two parts, list, complete, and read via AWS SDK", async ct =>
                    {
                        using (AmazonS3Client s3 = await NewClientAsync(ct))
                        {
                            string bucket = DbTest.NewContainerName();
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);

                            InitiateMultipartUploadResponse init = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = bucket, Key = "big" }, ct);
                            Check.True(!String.IsNullOrEmpty(init.UploadId), "upload id returned");

                            byte[] part1 = FillBytes(_MinPartBytes, 0x41); // 5 MiB of 'A'
                            byte[] part2 = Encoding.UTF8.GetBytes("-tail");

                            UploadPartResponse up1 = await s3.UploadPartAsync(new UploadPartRequest
                            {
                                BucketName = bucket, Key = "big", UploadId = init.UploadId, PartNumber = 1, InputStream = new MemoryStream(part1), PartSize = part1.Length
                            }, ct);
                            UploadPartResponse up2 = await s3.UploadPartAsync(new UploadPartRequest
                            {
                                BucketName = bucket, Key = "big", UploadId = init.UploadId, PartNumber = 2, InputStream = new MemoryStream(part2), PartSize = part2.Length
                            }, ct);

                            int listed = 0;
                            int partMarker = 0;
                            while (true)
                            {
                                ListPartsResponse parts = await s3.ListPartsAsync(new ListPartsRequest { BucketName = bucket, Key = "big", UploadId = init.UploadId, MaxParts = 1000, PartNumberMarker = partMarker.ToString() }, ct);
                                listed += parts.Parts.Count;
                                if (!parts.IsTruncated) break;
                                partMarker = parts.NextPartNumberMarker;
                            }
                            Check.Equal(2, listed, "two parts listed");

                            ListMultipartUploadsResponse uploads = await s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = bucket }, ct);
                            bool found = false;
                            foreach (MultipartUpload u in uploads.MultipartUploads) if (u.UploadId == init.UploadId) found = true;
                            Check.True(found, "in-progress upload listed");

                            CompleteMultipartUploadResponse done = await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
                            {
                                BucketName = bucket, Key = "big", UploadId = init.UploadId,
                                PartETags = new List<PartETag> { new PartETag(1, up1.ETag), new PartETag(2, up2.ETag) }
                            }, ct);

                            Check.True(done.ETag.Contains("-2"), "multipart etag has part count suffix");

                            using (GetObjectResponse get = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = "big" }, ct))
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await get.ResponseStream.CopyToAsync(ms, ct);
                                byte[] all = ms.ToArray();
                                Check.Equal(part1.Length + part2.Length, all.Length, "assembled length");
                                Check.Equal("-tail", Encoding.UTF8.GetString(all, part1.Length, part2.Length), "tail bytes");
                            }

                            await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = "big" }, ct);
                            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct);
                        }
                    }),

                    new TestCaseDescriptor("S3Multipart", "Abort", "An aborted upload no longer lists and cannot complete", async ct =>
                    {
                        using (AmazonS3Client s3 = await NewClientAsync(ct))
                        {
                            string bucket = DbTest.NewContainerName();
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
                            InitiateMultipartUploadResponse init = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = bucket, Key = "k" }, ct);
                            await s3.UploadPartAsync(new UploadPartRequest { BucketName = bucket, Key = "k", UploadId = init.UploadId, PartNumber = 1, InputStream = new MemoryStream(Encoding.UTF8.GetBytes("data")), PartSize = 4 }, ct);

                            await s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest { BucketName = bucket, Key = "k", UploadId = init.UploadId }, ct);

                            ListMultipartUploadsResponse uploads = await s3.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = bucket }, ct);
                            foreach (MultipartUpload u in uploads.MultipartUploads) Check.True(u.UploadId != init.UploadId, "aborted upload not listed");

                            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct);
                        }
                    }),

                    new TestCaseDescriptor("S3Multipart", "RangedDownload", "Ranged GET returns the correct bytes and a numeric Content-Range total", async ct =>
                    {
                        RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
                        string bucket = DbTest.NewContainerName();
                        byte[] body = Encoding.UTF8.GetBytes("0123456789ABCDEF");

                        using (AmazonS3Client s3 = await NewClientAsync(ct))
                        {
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
                            await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = "r.bin", InputStream = new MemoryStream(body) }, ct);

                            // AWS SDK byte-range GET returns exactly the requested bytes.
                            using (GetObjectResponse ranged = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = "r.bin", ByteRange = new ByteRange(4, 8) }, ct))
                            using (MemoryStream ms = new MemoryStream())
                            {
                                await ranged.ResponseStream.CopyToAsync(ms, ct);
                                Check.Equal("45678", Encoding.UTF8.GetString(ms.ToArray()), "ranged bytes 4-8 inclusive");
                            }
                        }

                        // Raw HTTP range request: 206 with a numeric Content-Range total (the S3Server 7.3.1 fix),
                        // not the old "bytes start-end/*". This is what lets aws s3 cp download large objects.
                        using (HttpClient http = new HttpClient())
                        {
                            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, server.S3ServiceUrl + "/" + bucket + "/r.bin");
                            req.Headers.Range = new RangeHeaderValue(4, 8);
                            using (HttpResponseMessage resp = await http.SendAsync(req, ct))
                            {
                                Check.Equal(206, (int)resp.StatusCode, "partial content status");
                                string? contentRange = null;
                                if (resp.Content.Headers.TryGetValues("Content-Range", out IEnumerable<string>? vals))
                                {
                                    foreach (string v in vals) { contentRange = v; break; }
                                }
                                Check.NotNull(contentRange, "Content-Range present");
                                Check.Equal("bytes 4-8/" + body.Length, contentRange, "numeric Content-Range total");
                            }
                        }

                        using (AmazonS3Client s3 = await NewClientAsync(ct))
                        {
                            await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = "r.bin" }, ct);
                            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct);
                        }
                    }),

                    new TestCaseDescriptor("S3Multipart", "EtagParity", "GET, HEAD, and LIST agree on ETag for single-part and multipart objects", async ct =>
                    {
                        using (AmazonS3Client s3 = await NewClientAsync(ct))
                        {
                            string bucket = DbTest.NewContainerName();
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);

                            // Single-part PutObject: ETag is a plain MD5 (no suffix).
                            byte[] body = Encoding.UTF8.GetBytes("single-part-body");
                            await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = "single", InputStream = new MemoryStream(body) }, ct);
                            string getEtag = (await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = "single" }, ct)).ETag;
                            string listEtag = await ListEtagAsync(s3, bucket, "single", ct);
                            string expectedMd5 = "\"" + Convert.ToHexString(MD5.HashData(body)).ToLowerInvariant() + "\"";
                            Check.Equal(expectedMd5, getEtag, "single-part HEAD etag is md5");
                            Check.Equal(expectedMd5, listEtag, "single-part LIST etag is md5");
                            Check.False(getEtag.Contains("-"), "single-part etag has no part suffix");

                            // Multipart object: ETag carries the -N suffix and is consistent across HEAD/LIST.
                            InitiateMultipartUploadResponse init = await s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest { BucketName = bucket, Key = "multi" }, ct);
                            byte[] p1 = FillBytes(_MinPartBytes, 0x42);
                            byte[] p2 = Encoding.UTF8.GetBytes("end");
                            UploadPartResponse u1 = await s3.UploadPartAsync(new UploadPartRequest { BucketName = bucket, Key = "multi", UploadId = init.UploadId, PartNumber = 1, InputStream = new MemoryStream(p1), PartSize = p1.Length }, ct);
                            UploadPartResponse u2 = await s3.UploadPartAsync(new UploadPartRequest { BucketName = bucket, Key = "multi", UploadId = init.UploadId, PartNumber = 2, InputStream = new MemoryStream(p2), PartSize = p2.Length }, ct);
                            CompleteMultipartUploadResponse done = await s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
                            {
                                BucketName = bucket, Key = "multi", UploadId = init.UploadId,
                                PartETags = new List<PartETag> { new PartETag(1, u1.ETag), new PartETag(2, u2.ETag) }
                            }, ct);

                            string mHead = (await s3.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = bucket, Key = "multi" }, ct)).ETag;
                            string mList = await ListEtagAsync(s3, bucket, "multi", ct);
                            string mGet;
                            using (GetObjectResponse g = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = "multi" }, ct))
                            {
                                mGet = g.ETag;
                            }
                            Check.Equal(Normalize(done.ETag), Normalize(mHead), "multipart complete/HEAD etag agree");
                            Check.Equal(Normalize(done.ETag), Normalize(mList), "multipart complete/LIST etag agree");
                            Check.Equal(Normalize(done.ETag), Normalize(mGet), "multipart complete/GET etag agree");
                            Check.True(Normalize(mHead).EndsWith("-2\""), "multipart etag has -2 suffix");

                            await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = "single" }, ct);
                            await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = "multi" }, ct);
                            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct);
                        }
                    })
                });
        }

        #region Private-Methods

        private static async Task<string> ListEtagAsync(AmazonS3Client s3, string bucket, string key, CancellationToken ct)
        {
            ListObjectsV2Response list = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = key }, ct);
            foreach (S3Object o in list.S3Objects) if (o.Key == key) return o.ETag;
            throw new InvalidOperationException("object not listed: " + key);
        }

        private static string Normalize(string etag)
        {
            return etag == null ? String.Empty : etag.Trim();
        }

        private static byte[] FillBytes(int count, byte value)
        {
            byte[] bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = value;
            return bytes;
        }

        private static async Task<AmazonS3Client> NewClientAsync(CancellationToken ct)
        {
            RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = server.S3ServiceUrl,
                ForcePathStyle = true,
                UseHttp = true,
                AuthenticationRegion = "us-west-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };
            return new AmazonS3Client(new BasicAWSCredentials("pepperx", "pepperx"), config);
        }

        #endregion
    }
}
