namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Touchstone.Core;

    /// <summary>
    /// Verifies the S3-compatible surface using the AWS SDK for .NET against an in-process server.
    /// </summary>
    public static class S3ProtocolSuite
    {
        private static RestTestServer? _Server;

        /// <summary>
        /// Build the S3 protocol suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("S3Protocol", "S3 protocol", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("S3Protocol", "Unavailable", "S3 protocol (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "S3Protocol",
                displayName: "S3 protocol",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("S3Protocol", "BucketLifecycle", "Bucket create, list, and delete via AWS SDK", async ct =>
                    {
                        using (AmazonS3Client s3 = NewClient())
                        {
                            string bucket = DbTest.NewContainerName();
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);

                            ListBucketsResponse buckets = await s3.ListBucketsAsync(ct);
                            bool found = false;
                            foreach (S3Bucket b in buckets.Buckets) if (b.BucketName == bucket) found = true;
                            Check.True(found, "bucket listed");

                            await s3.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket }, ct);
                        }
                    }),

                    new TestCaseDescriptor("S3Protocol", "ObjectLifecycle", "Put, get, and delete an object via AWS SDK", async ct =>
                    {
                        using (AmazonS3Client s3 = NewClient())
                        {
                            string bucket = DbTest.NewContainerName();
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);

                            await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = "a/b/c.txt", ContentBody = "s3-payload", ContentType = "text/plain" }, ct);

                            using (GetObjectResponse get = await s3.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = "a/b/c.txt" }, ct))
                            using (StreamReader reader = new StreamReader(get.ResponseStream))
                            {
                                string content = await reader.ReadToEndAsync(ct);
                                Check.Equal("s3-payload", content, "object content round-trips via S3");
                            }

                            await s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = "a/b/c.txt" }, ct);
                        }
                    }),

                    new TestCaseDescriptor("S3Protocol", "CrossProtocol", "Object written via S3 is readable via REST", async ct =>
                    {
                        string bucket = DbTest.NewContainerName();
                        using (AmazonS3Client s3 = NewClient())
                        {
                            await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket }, ct);
                            await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = "shared", ContentBody = "cross-protocol", ContentType = "text/plain" }, ct);
                        }

                        System.Net.Http.HttpResponseMessage read = await _Server!.Client.GetAsync("/v1.0/containers/" + bucket + "/object?key=shared", ct);
                        Check.Equal("cross-protocol", await read.Content.ReadAsStringAsync(ct), "S3-written object read via REST");
                    })
                },
                beforeSuiteAsync: async ct =>
                {
                    _Server = await RestTestServer.StartAsync(true, false, ct).ConfigureAwait(false);
                },
                afterSuiteAsync: async ct =>
                {
                    if (_Server != null)
                    {
                        await _Server.DisposeAsync().ConfigureAwait(false);
                        _Server = null;
                    }
                });
        }

        private static AmazonS3Client NewClient()
        {
            if (_Server == null) throw new Exception("Server not started.");
            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = _Server.S3ServiceUrl,
                ForcePathStyle = true,
                UseHttp = true,
                AuthenticationRegion = "us-west-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
            };
            return new AmazonS3Client(new BasicAWSCredentials("pepperx", "pepperx"), config);
        }
    }
}
