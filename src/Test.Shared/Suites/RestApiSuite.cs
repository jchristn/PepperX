namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Boots an in-process server and verifies the native REST surface end to end.
    /// </summary>
    public static class RestApiSuite
    {

        /// <summary>
        /// Build the REST API suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            if (!PostgresTestFixture.IsAvailable())
            {
                return new TestSuiteDescriptor("RestApi", "REST API", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RestApi", "Unavailable", "REST API (database unavailable)", _ => Task.CompletedTask, skip: true, skipReason: "PostgreSQL test database unavailable")
                });
            }

            return new TestSuiteDescriptor(
                suiteId: "RestApi",
                displayName: "REST API",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("RestApi", "Health", "Health endpoints respond", async ct =>
                    {
                        HttpResponseMessage root = await (await ClientAsync(ct)).GetAsync("/", ct);
                        Check.True(root.IsSuccessStatusCode, "GET / ok");
                        string body = await root.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("PepperX", StringComparison.Ordinal), "product name present");
                    }),

                    new TestCaseDescriptor("RestApi", "ContainerCrud", "Container create, read, list, delete", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        HttpResponseMessage create = await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);
                        Check.Equal(HttpStatusCode.Created, create.StatusCode, "container created");

                        HttpResponseMessage read = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name, ct);
                        Check.True(read.IsSuccessStatusCode, "container read");

                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/containers", ct);
                        Check.True((await list.Content.ReadAsStringAsync(ct)).Contains(name, StringComparison.Ordinal), "container listed");

                        HttpResponseMessage delete = await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name, ct);
                        Check.Equal(HttpStatusCode.NoContent, delete.StatusCode, "container deleted");
                    }),

                    new TestCaseDescriptor("RestApi", "MultipartUploads", "In-progress multipart uploads list and abort routes are wired", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        // A container with no uploads returns an empty, well-formed page (not a 404).
                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/multipart-uploads", ct);
                        Check.True(list.IsSuccessStatusCode, "empty uploads list ok");
                        Check.True((await list.Content.ReadAsStringAsync(ct)).Contains("uploads", StringComparison.OrdinalIgnoreCase), "uploads page shape");

                        // Abort is idempotent — aborting an unknown upload succeeds (204).
                        HttpResponseMessage abort = await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name + "/multipart-uploads/mpu_does_not_exist", ct);
                        Check.Equal(HttpStatusCode.NoContent, abort.StatusCode, "abort unknown upload is no-op");

                        // Listing uploads for a missing container is a 404.
                        HttpResponseMessage missing = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/no-such-container-xyz/multipart-uploads", ct);
                        Check.Equal(HttpStatusCode.NotFound, missing.StatusCode, "missing container 404");

                        await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name, ct);
                    }),

                    new TestCaseDescriptor("RestApi", "MultipartLifecycle", "Multipart initiate, upload part, copy part, list parts, complete, and read over REST", async ct =>
                    {
                        HttpClient client = await ClientAsync(ct);
                        string name = DbTest.NewContainerName();
                        await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        // A small source object for the copy-part step.
                        byte[] copied = Encoding.UTF8.GetBytes("COPIED");
                        await client.PutAsync("/v1.0/containers/" + name + "/object?key=src", new ByteArrayContent(copied), ct);

                        // Initiate.
                        HttpResponseMessage init = await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads?key=assembled&contentType=application/octet-stream", Json("{}"), ct);
                        Check.Equal(HttpStatusCode.Created, init.StatusCode, "initiate 201");
                        string uploadId = await JsonStringAsync(init, "UploadId", ct);
                        Check.True(uploadId.StartsWith("mpu_", StringComparison.Ordinal), "upload id returned");

                        // Part 1: a 5 MiB inline body (satisfies the non-final minimum part size).
                        byte[] part1 = new byte[5 * 1024 * 1024];
                        for (int i = 0; i < part1.Length; i++) part1[i] = 0x41;
                        HttpResponseMessage up1 = await client.PutAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/1", new ByteArrayContent(part1), ct);
                        Check.Equal(HttpStatusCode.OK, up1.StatusCode, "upload part 1 200");
                        string etag1 = await JsonStringAsync(up1, "ETag", ct);

                        // Part 2 (last): copied from the source object via the copy-source header.
                        HttpRequestMessage copyReq = new HttpRequestMessage(HttpMethod.Put, "/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/2");
                        copyReq.Headers.TryAddWithoutValidation("x-pepperx-copy-source", name + "/src");
                        HttpResponseMessage up2 = await client.SendAsync(copyReq, ct);
                        Check.Equal(HttpStatusCode.OK, up2.StatusCode, "upload part 2 (copy) 200");
                        string etag2 = await JsonStringAsync(up2, "ETag", ct);

                        // List parts shows both.
                        HttpResponseMessage parts = await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts", ct);
                        string partsBody = await parts.Content.ReadAsStringAsync(ct);
                        Check.True(partsBody.Contains("\"PartNumber\":1", StringComparison.Ordinal) && partsBody.Contains("\"PartNumber\":2", StringComparison.Ordinal), "both parts listed");

                        // Complete.
                        string completeBody = "{\"Parts\":[{\"PartNumber\":1,\"ETag\":\"" + etag1 + "\"},{\"PartNumber\":2,\"ETag\":\"" + etag2 + "\"}]}";
                        HttpResponseMessage complete = await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/complete", Json(completeBody), ct);
                        Check.Equal(HttpStatusCode.OK, complete.StatusCode, "complete 200");

                        // Read the assembled object: 5 MiB of 'A' followed by "COPIED".
                        HttpResponseMessage obj = await client.GetAsync("/v1.0/containers/" + name + "/object?key=assembled", ct);
                        byte[] assembled = await obj.Content.ReadAsByteArrayAsync(ct);
                        Check.Equal(part1.Length + copied.Length, assembled.Length, "assembled length");
                        Check.Equal("COPIED", Encoding.UTF8.GetString(assembled, part1.Length, copied.Length), "assembled tail is the copied part");

                        await client.DeleteAsync("/v1.0/containers/" + name + "?force=true", ct);
                    }),

                    new TestCaseDescriptor("RestApi", "MultipartPartManagement", "Per-part metadata (with SHA-256) and discrete part deletion over REST", async ct =>
                    {
                        HttpClient client = await ClientAsync(ct);
                        string name = DbTest.NewContainerName();
                        await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        HttpResponseMessage init = await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads?key=k", Json("{}"), ct);
                        string uploadId = await JsonStringAsync(init, "UploadId", ct);

                        await client.PutAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/1", new ByteArrayContent(Encoding.UTF8.GetBytes("part-one")), ct);
                        await client.PutAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/2", new ByteArrayContent(Encoding.UTF8.GetBytes("part-two")), ct);

                        // Single-part metadata carries both hashes.
                        HttpResponseMessage getPart = await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/1", ct);
                        Check.Equal(HttpStatusCode.OK, getPart.StatusCode, "get part 1 200");
                        string partBody = await getPart.Content.ReadAsStringAsync(ct);
                        Check.True(partBody.Contains("\"Md5\"", StringComparison.Ordinal) && partBody.Contains("\"Sha256\"", StringComparison.Ordinal), "part metadata carries md5 and sha256");

                        string listBody = await (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts", ct)).Content.ReadAsStringAsync(ct);
                        Check.True(listBody.Contains("\"PartNumber\":1", StringComparison.Ordinal) && listBody.Contains("\"PartNumber\":2", StringComparison.Ordinal), "both parts listed");

                        // Delete part 1 discretely; part 2 remains.
                        HttpResponseMessage del = await client.DeleteAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/1", ct);
                        Check.Equal(HttpStatusCode.NoContent, del.StatusCode, "delete part 1 204");
                        Check.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/1", ct)).StatusCode, "part 1 gone");
                        Check.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/2", ct)).StatusCode, "part 2 remains");
                        Check.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/parts/99", ct)).StatusCode, "missing part 404");

                        await client.DeleteAsync("/v1.0/containers/" + name + "?force=true", ct);
                    }),

                    new TestCaseDescriptor("RestApi", "MultipartAbort", "An initiated upload lists, aborts, and then cannot complete over REST", async ct =>
                    {
                        HttpClient client = await ClientAsync(ct);
                        string name = DbTest.NewContainerName();
                        await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        HttpResponseMessage init = await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads?key=pending", Json("{}"), ct);
                        string uploadId = await JsonStringAsync(init, "UploadId", ct);

                        string listed = await (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads", ct)).Content.ReadAsStringAsync(ct);
                        Check.True(listed.Contains("pending", StringComparison.Ordinal), "in-progress upload listed");

                        HttpResponseMessage abort = await client.DeleteAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId, ct);
                        Check.Equal(HttpStatusCode.NoContent, abort.StatusCode, "abort 204");

                        string after = await (await client.GetAsync("/v1.0/containers/" + name + "/multipart-uploads", ct)).Content.ReadAsStringAsync(ct);
                        Check.False(after.Contains("pending", StringComparison.Ordinal), "upload gone after abort");

                        HttpResponseMessage complete = await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads/" + uploadId + "/complete", Json("{\"Parts\":[]}"), ct);
                        Check.Equal(HttpStatusCode.NotFound, complete.StatusCode, "complete after abort 404");

                        await client.DeleteAsync("/v1.0/containers/" + name + "?force=true", ct);
                    }),

                    new TestCaseDescriptor("RestApi", "ContainerDeleteWithInProgressUpload", "Force-deleting a container purges its in-progress multipart uploads", async ct =>
                    {
                        HttpClient client = await ClientAsync(ct);
                        string name = DbTest.NewContainerName();
                        await client.PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        await client.PostAsync("/v1.0/containers/" + name + "/multipart-uploads?key=leftover", Json("{}"), ct);

                        // Previously this returned 500 due to the multipart_uploads -> containers foreign key.
                        HttpResponseMessage delete = await client.DeleteAsync("/v1.0/containers/" + name + "?force=true", ct);
                        Check.Equal(HttpStatusCode.NoContent, delete.StatusCode, "container deleted despite in-progress upload");

                        HttpResponseMessage gone = await client.GetAsync("/v1.0/containers/" + name, ct);
                        Check.Equal(HttpStatusCode.NotFound, gone.StatusCode, "container gone");
                    }),

                    new TestCaseDescriptor("RestApi", "ObjectLifecycle", "Object write, read (slashed key), metadata, delete", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        string key = Uri.EscapeDataString("a/b/c.bin");
                        HttpResponseMessage write = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=" + key,
                            new StringContent("payload-data", Encoding.UTF8, "text/plain"), ct);
                        Check.Equal(HttpStatusCode.Created, write.StatusCode, "object written");

                        HttpResponseMessage read = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal("payload-data", await read.Content.ReadAsStringAsync(ct), "payload round-trips");

                        HttpResponseMessage meta = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object/metadata?key=" + key, ct);
                        Check.True((await meta.Content.ReadAsStringAsync(ct)).Contains("a/b/c.bin", StringComparison.Ordinal), "metadata key present");

                        HttpResponseMessage delete = await (await ClientAsync(ct)).DeleteAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal(HttpStatusCode.NoContent, delete.StatusCode, "object deleted");

                        HttpResponseMessage gone = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/object?key=" + key, ct);
                        Check.Equal(HttpStatusCode.NotFound, gone.StatusCode, "object gone");
                    }),

                    new TestCaseDescriptor("RestApi", "ContainerCache", "Container cache settings round-trip and reject bad input", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);

                        // A container created without cache settings defaults to caching enabled (D9).
                        HttpResponseMessage get = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/cache", ct);
                        Check.True(get.IsSuccessStatusCode, "cache settings served");
                        string body = await get.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("\"Enabled\":true", StringComparison.Ordinal), "caching enabled by default");
                        Check.True(body.Contains("\"HitCount\"", StringComparison.Ordinal), "live statistics included");

                        // Replace the settings.
                        HttpResponseMessage put = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/cache",
                            Json("{\"Enabled\":true,\"Policy\":\"FIFO\",\"MaxObjects\":321,\"MaxMemoryBytes\":0,\"EvictCount\":7,\"MaxCacheableObjectBytes\":2048}"), ct);
                        Check.True(put.IsSuccessStatusCode, "cache update accepted");
                        string updated = await put.Content.ReadAsStringAsync(ct);
                        Check.True(updated.Contains("\"Policy\":\"FIFO\"", StringComparison.Ordinal), "policy applied");
                        Check.True(updated.Contains("\"MaxObjects\":321", StringComparison.Ordinal), "capacity applied");

                        // The change is durable across reads.
                        HttpResponseMessage after = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + name + "/cache", ct);
                        Check.True((await after.Content.ReadAsStringAsync(ct)).Contains("\"MaxObjects\":321", StringComparison.Ordinal), "cache change durable");

                        // Invalid input (evict > max objects) is rejected with 400.
                        HttpResponseMessage bad = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/cache",
                            Json("{\"Enabled\":true,\"Policy\":\"LRU\",\"MaxObjects\":10,\"EvictCount\":100,\"MaxCacheableObjectBytes\":1048576}"), ct);
                        Check.Equal((int)HttpStatusCode.BadRequest, (int)bad.StatusCode, "invalid cache settings rejected with 400");

                        // A missing container is a 404.
                        HttpResponseMessage missing = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/no-such-container-xyz/cache", ct);
                        Check.Equal((int)HttpStatusCode.NotFound, (int)missing.StatusCode, "missing container is 404");
                    }),

                    new TestCaseDescriptor("RestApi", "RespIndex", "A container's RESP index is assignable, unique, and clearable", async ct =>
                    {
                        // A random, per-invocation index: it is globally unique and the shared server DB is
                        // reused across cases (and the xUnit adapter runs each case twice), so a fixed value
                        // would collide with the other run.
                        int idx = Random.Shared.Next(1_000_000, int.MaxValue);
                        string a = DbTest.NewContainerName();
                        string b = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + a + "\"}"), ct);
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + b + "\"}"), ct);

                        HttpResponseMessage assign = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + a + "/resp-index", Json("{\"Index\":" + idx + "}"), ct);
                        Check.True(assign.IsSuccessStatusCode, "index assigned");
                        Check.True((await assign.Content.ReadAsStringAsync(ct)).Contains("\"RespDatabaseIndex\":" + idx, StringComparison.Ordinal), "index echoed on the container");

                        // The same index on another container conflicts.
                        HttpResponseMessage conflict = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + b + "/resp-index", Json("{\"Index\":" + idx + "}"), ct);
                        Check.Equal((int)HttpStatusCode.Conflict, (int)conflict.StatusCode, "duplicate index is 409");

                        // A negative index is rejected.
                        HttpResponseMessage negative = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + a + "/resp-index", Json("{\"Index\":-1}"), ct);
                        Check.Equal((int)HttpStatusCode.BadRequest, (int)negative.StatusCode, "negative index is 400");

                        // Clearing frees the index for another container.
                        HttpResponseMessage clear = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + a + "/resp-index", Json("{\"Index\":null}"), ct);
                        Check.True(clear.IsSuccessStatusCode, "index cleared");
                        HttpResponseMessage reassign = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + b + "/resp-index", Json("{\"Index\":" + idx + "}"), ct);
                        Check.True(reassign.IsSuccessStatusCode, "freed index reassignable");
                    }),

                    new TestCaseDescriptor("RestApi", "MultipartExpiryPerContainer", "Per-container multipart expiry is settable and drives an upload's expiry window", async ct =>
                    {
                        // Create a container with an explicit 3-day expiry; the response echoes it.
                        string a = DbTest.NewContainerName();
                        HttpResponseMessage createA = await (await ClientAsync(ct)).PutAsync(
                            "/v1.0/containers", Json("{\"Name\":\"" + a + "\",\"MultipartUploadExpiryDays\":3}"), ct);
                        Check.True(createA.IsSuccessStatusCode, "container created");
                        Check.True((await createA.Content.ReadAsStringAsync(ct)).Contains("\"MultipartUploadExpiryDays\":3", StringComparison.Ordinal), "expiry echoed on create");

                        // A container created without the field inherits: the serializer omits null-valued
                        // properties, so the field is absent (not written as null) on the response.
                        string b = DbTest.NewContainerName();
                        HttpResponseMessage createB = await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + b + "\"}"), ct);
                        Check.False((await createB.Content.ReadAsStringAsync(ct)).Contains("\"MultipartUploadExpiryDays\"", StringComparison.Ordinal), "unset expiry is omitted");

                        // The dedicated endpoint sets and clears the override.
                        HttpResponseMessage set = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + b + "/multipart-expiry", Json("{\"Days\":10}"), ct);
                        Check.True(set.IsSuccessStatusCode, "expiry set");
                        Check.True((await set.Content.ReadAsStringAsync(ct)).Contains("\"MultipartUploadExpiryDays\":10", StringComparison.Ordinal), "set value echoed");

                        HttpResponseMessage clear = await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + b + "/multipart-expiry", Json("{\"Days\":null}"), ct);
                        Check.False((await clear.Content.ReadAsStringAsync(ct)).Contains("\"MultipartUploadExpiryDays\"", StringComparison.Ordinal), "cleared back to inherit (field omitted)");

                        // Behavior: an upload initiated in the 3-day container expires ~3 days after it starts.
                        HttpResponseMessage initiate = await (await ClientAsync(ct)).PostAsync(
                            "/v1.0/containers/" + a + "/multipart-uploads?key=big.bin", Json("{}"), ct);
                        Check.True(initiate.IsSuccessStatusCode, "upload initiated");

                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + a + "/multipart-uploads?maxUploads=10", ct);
                        using (JsonDocument doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct)))
                        {
                            JsonElement upload = doc.RootElement.GetProperty("Uploads")[0];
                            DateTime initiated = upload.GetProperty("InitiatedUtc").GetDateTime();
                            DateTime expires = upload.GetProperty("ExpiresUtc").GetDateTime();
                            double days = (expires - initiated).TotalDays;
                            Check.True(days > 2.9 && days < 3.1, "upload window reflects the container's 3-day expiry (was " + days.ToString("0.00") + ")");
                        }
                    }),

                    new TestCaseDescriptor("RestApi", "Enumerate", "Object enumeration filters by label", async ct =>
                    {
                        string name = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + name + "\"}"), ct);
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=red", new StringContent("x"), ct);
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + name + "/object?key=blue&", new StringContent("y"), ct);
                        await (await ClientAsync(ct)).PostAsync("/v1.0/containers/" + name + "/object?key=tagged",
                            Json("{\"ContentType\":\"text/plain\",\"Labels\":[\"special\"],\"DataBase64\":\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes("z")) + "\"}"), ct);

                        HttpResponseMessage search = await (await ClientAsync(ct)).PostAsync("/v1.0/containers/" + name + "/objects/enumerate",
                            Json("{\"Labels\":[\"special\"]}"), ct);
                        string body = await search.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("\"TotalRecords\":1", StringComparison.Ordinal), "label filter returns one");
                        Check.True(body.Contains("tagged", StringComparison.Ordinal), "correct object");
                    }),

                    new TestCaseDescriptor("RestApi", "RequestHistory", "Requests are captured", async ct =>
                    {
                        await (await ClientAsync(ct)).GetAsync("/v1.0/api/health", ct);
                        await Task.Delay(500, ct);
                        HttpResponseMessage list = await (await ClientAsync(ct)).GetAsync("/v1.0/api/request-history?pathContains=/v1.0/api/health", ct);
                        string body = await list.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("\"TotalCount\":", StringComparison.Ordinal), "history page shape");
                    }),

                    new TestCaseDescriptor("RestApi", "OpenApi", "OpenAPI document is served", async ct =>
                    {
                        HttpResponseMessage spec = await (await ClientAsync(ct)).GetAsync("/openapi.json", ct);
                        Check.True(spec.IsSuccessStatusCode, "openapi served");
                        string body = await spec.Content.ReadAsStringAsync(ct);
                        Check.True(body.Contains("/v1.0/containers", StringComparison.Ordinal), "paths present");
                        Check.True(body.Contains("PepperX REST API", StringComparison.Ordinal), "title present");
                    }),

                    new TestCaseDescriptor("RestApi", "Cors", "CORS origin header is sent exactly once", async ct =>
                    {
                        // The dashboard fetches these two from a different origin. Watson's OpenAPI
                        // handler emits its own Access-Control-Allow-Origin, so adding ours as well
                        // produced "*, *" -- which browsers reject as a malformed origin list, and
                        // which curl and server-side clients never notice.
                        foreach (string path in new[] { "/openapi.json", "/v1.0/admin/stats" })
                        {
                            HttpResponseMessage response = await (await ClientAsync(ct)).GetAsync(path, ct);
                            Check.True(
                                response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? origins),
                                "CORS header present on " + path);

                            List<string> values = new List<string>(origins!);
                            Check.Equal(1, values.Count, "single CORS origin header on " + path);
                            Check.Equal("*", values[0], "permissive CORS origin on " + path);
                        }
                    }),

                    new TestCaseDescriptor("RestApi", "ResponseHeaders", "Responses do not echo request headers", async ct =>
                    {
                        // Watson seeds responses with default headers that are request headers by
                        // nature. Echoing them back is not merely untidy: "Connection: close" on a
                        // keep-alive connection makes strict HTTP parsers reject the whole response
                        // (Node refused every object read with "Data after Connection: close"), and
                        // "Host" leaks the node's internal hostname to every caller.
                        HttpResponseMessage response = await (await ClientAsync(ct)).GetAsync("/v1.0/api/health", ct);

                        foreach (string header in new[] { "Accept", "Accept-Language", "Accept-Charset", "Cache-Control", "User-Agent" })
                        {
                            Check.False(response.Headers.Contains(header), "no echoed " + header + " header");
                        }
                    }),

                    new TestCaseDescriptor("RestApi", "ObjectFraming", "Object reads are length-declared and single-terminated", async ct =>
                    {
                        // The read path hand-rolled chunked framing and then returned null, so the
                        // typed-route wrapper appended a second terminator. curl tolerated the extra
                        // bytes; Node's parser rejected the response outright. A length-declared send
                        // avoids the whole class of problem, so the framing is asserted directly.
                        string container = DbTest.NewContainerName();
                        await (await ClientAsync(ct)).PutAsync("/v1.0/containers", Json("{\"Name\":\"" + container + "\"}"), ct);

                        byte[] payload = Encoding.UTF8.GetBytes("framing check payload");
                        using (ByteArrayContent content = new ByteArrayContent(payload))
                        {
                            await (await ClientAsync(ct)).PutAsync("/v1.0/containers/" + container + "/object?key=framed.bin", content, ct);
                        }

                        HttpResponseMessage read = await (await ClientAsync(ct)).GetAsync("/v1.0/containers/" + container + "/object?key=framed.bin", ct);
                        Check.True(read.IsSuccessStatusCode, "object read succeeded");
                        Check.Equal(false, read.Headers.TransferEncodingChunked ?? false, "response is not chunked");
                        Check.Equal((long)payload.Length, read.Content.Headers.ContentLength ?? -1, "content length declares the payload size");

                        byte[] received = await read.Content.ReadAsByteArrayAsync(ct);
                        Check.Equal(payload.Length, received.Length, "exactly the payload came back, with no trailing bytes");
                    }),

                    new TestCaseDescriptor("RestApi", "SettingsUpdate", "A settings update persists and reads back", async ct =>
                    {
                        // The update endpoint persists a partial change; only the supplied fields move.
                        // Verifying the read-back rather than the file keeps the test independent of where
                        // the node happens to store its settings.
                        HttpResponseMessage before = await (await ClientAsync(ct)).GetAsync("/v1.0/admin/settings", ct);
                        string beforeBody = await before.Content.ReadAsStringAsync(ct);
                        bool startedEnabled = beforeBody.Contains("\"RequestHistoryEnabled\":true", StringComparison.Ordinal);

                        // Flip retention to a distinctive value and toggle checksum verification.
                        HttpResponseMessage put = await (await ClientAsync(ct)).PutAsync(
                            "/v1.0/admin/settings",
                            Json("{\"RequestHistoryRetentionDays\":99,\"VerifyChecksumOnRead\":true,\"LogMinimumSeverity\":\"Warn\"}"),
                            ct);
                        Check.True(put.IsSuccessStatusCode, "update accepted");

                        string updated = await put.Content.ReadAsStringAsync(ct);
                        Check.True(updated.Contains("\"RequestHistoryRetentionDays\":99", StringComparison.Ordinal), "retention changed");
                        Check.True(updated.Contains("\"VerifyChecksumOnRead\":true", StringComparison.Ordinal), "checksum flag changed");
                        Check.True(updated.Contains("\"LogMinimumSeverity\":\"Warn\"", StringComparison.Ordinal), "log level changed");

                        // Unsupplied fields must not move: the request-history enabled flag is untouched.
                        Check.Equal(
                            startedEnabled,
                            updated.Contains("\"RequestHistoryEnabled\":true", StringComparison.Ordinal),
                            "unspecified fields are left alone");

                        // A fresh read reflects the persisted change.
                        HttpResponseMessage after = await (await ClientAsync(ct)).GetAsync("/v1.0/admin/settings", ct);
                        string afterBody = await after.Content.ReadAsStringAsync(ct);
                        Check.True(afterBody.Contains("\"RequestHistoryRetentionDays\":99", StringComparison.Ordinal), "change is durable across reads");
                    }),

                    new TestCaseDescriptor("RestApi", "RawSettingsRoundTrip", "The full settings document round-trips", async ct =>
                    {
                        // The raw endpoint is the whole file so every field is editable. Read it, change
                        // a deeply nested value the curated update does not cover, write it back, and
                        // confirm the change is reflected -- proving the entirety is editable, not a
                        // subset.
                        HttpResponseMessage get = await (await ClientAsync(ct)).GetAsync("/v1.0/admin/settings/raw", ct);
                        Check.True(get.IsSuccessStatusCode, "raw settings served");
                        string raw = await get.Content.ReadAsStringAsync(ct);
                        Check.True(raw.Contains("\"Rest\"", StringComparison.Ordinal), "full document includes protocol sections");

                        // Flip a nested value the partial update has no field for.
                        string mutated = raw.Replace("\"Region\":\"us-west-1\"", "\"Region\":\"eu-central-1\"", StringComparison.Ordinal);
                        Check.True(!ReferenceEquals(mutated, raw) && mutated != raw, "found the region to change");

                        HttpResponseMessage put = await (await ClientAsync(ct)).PutAsync("/v1.0/admin/settings/raw", Json(mutated), ct);
                        Check.True(put.IsSuccessStatusCode, "full document accepted");

                        HttpResponseMessage after = await (await ClientAsync(ct)).GetAsync("/v1.0/admin/settings/raw", ct);
                        string afterBody = await after.Content.ReadAsStringAsync(ct);
                        Check.True(afterBody.Contains("eu-central-1", StringComparison.Ordinal), "nested change persisted");
                    }),

                    new TestCaseDescriptor("RestApi", "RawSettingsRejectsGarbage", "A malformed settings document is refused", async ct =>
                    {
                        // Validation is the deserialize: a body that cannot become settings is rejected
                        // before anything is written, so a typo cannot leave a node with a file it can no
                        // longer start from.
                        HttpResponseMessage put = await (await ClientAsync(ct)).PutAsync("/v1.0/admin/settings/raw", Json("{ not valid"), ct);
                        Check.Equal((int)HttpStatusCode.BadRequest, (int)put.StatusCode, "garbage rejected with 400");
                    }),

                    new TestCaseDescriptor("RestApi", "Settings", "Settings expose protocols without credentials", async ct =>
                    {
                        HttpResponseMessage response = await (await ClientAsync(ct)).GetAsync("/v1.0/admin/settings", ct);
                        Check.True(response.IsSuccessStatusCode, "settings served");

                        string body = await response.Content.ReadAsStringAsync(ct);
                        foreach (string protocol in new[] { "REST", "S3", "RESP", "WebSockets", "MCP" })
                        {
                            Check.True(body.Contains("\"" + protocol + "\"", StringComparison.Ordinal), protocol + " listed");
                        }

                        Check.True(body.Contains("\"NodeId\"", StringComparison.Ordinal), "node identity present");
                        Check.True(body.Contains("\"DatabaseName\"", StringComparison.Ordinal), "database named");

                        // The whole point of a separate response type rather than serializing settings
                        // directly: the database password and S3 static keys must not travel to an
                        // unauthenticated dashboard.
                        Check.False(body.Contains("Password", StringComparison.OrdinalIgnoreCase), "no password field");
                        Check.False(body.Contains("SecretKey", StringComparison.OrdinalIgnoreCase), "no S3 secret");
                        Check.False(body.Contains("AccessKey", StringComparison.OrdinalIgnoreCase), "no S3 access key");
                    })
                });
        }

        private static async Task<HttpClient> ClientAsync(CancellationToken ct)
        {
            RestTestServer server = await SharedServer.GetAsync(ct).ConfigureAwait(false);
            return server.Client;
        }

        private static StringContent Json(string json)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static async Task<string> JsonStringAsync(HttpResponseMessage response, string property, CancellationToken ct)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                return doc.RootElement.GetProperty(property).GetString() ?? String.Empty;
            }
        }
    }
}
