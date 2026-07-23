# PepperX C# SDK

Typed .NET client for [PepperX](../../README.md). Two clients ship in the package:

- **`PepperXRestClient`** — the full native REST surface, including streaming reads and writes.
- **`PepperXWebsocketClient`** — the same operations over one persistent connection, with many
  requests in flight at once.

Every method returns a typed object. Server errors arrive as `PepperXException`, which carries the
server's `ApiErrorEnum` classification and HTTP status code, so you branch on values rather than
parsing message text.

PepperX is unauthenticated by design — it is backend infrastructure, and access control belongs to
the application in front of it. There are no credentials to configure.

## Install

```bash
dotnet add package PepperX.Sdk
```

Targets `net8.0` and `net10.0`.

## REST quickstart

```csharp
using PepperX.Sdk;
using PepperX.Sdk.Models;

using (PepperXRestClient client = new PepperXRestClient("http://localhost:8000"))
{
    await client.CreateContainerAsync("photos");

    ObjectWriteResponse write = await client.WriteObjectAsync(
        "photos",
        "2026/07/cat.jpg",                       // keys may contain slashes
        File.ReadAllBytes("cat.jpg"),
        new WriteObjectRequest
        {
            ContentType = "image/jpeg",
            Labels = new List<string> { "animal", "cute" },
            Tags = new Dictionary<string, string> { { "team", "mammals" } },
            Object = new { camera = "X100V", iso = 400 }   // freeform JSON metadata
        });

    ObjectReadResult? read = await client.ReadObjectAsync("photos", "2026/07/cat.jpg");
    Console.WriteLine(read!.Data.Length + " bytes, sha256 " + read.Sha256);
}
```

### Searching by labels and tags

Both filters use AND semantics: every label listed must be present, and every tag key must be
present with the given value.

```csharp
EnumerationResult<ObjectMetadata> found = await client.EnumerateObjectsAsync("photos",
    new EnumerationQuery
    {
        Labels = new List<string> { "animal" },
        Tags = new Dictionary<string, string> { { "team", "mammals" } },
        MaxResults = 50
    });

Console.WriteLine(found.TotalRecords + " matches, " + found.RecordsRemaining + " remaining");
```

Use `SearchAsync` for the same query across every container, optionally narrowed by setting
`EnumerationQuery.Containers`.

### Streaming large objects

Neither direction buffers the payload in memory.

```csharp
using (FileStream source = File.OpenRead("archive.tar"))
{
    await client.WriteObjectAsync("backups", "archive.tar", source);
}

using (Stream? download = await client.OpenObjectAsync("backups", "archive.tar"))
using (FileStream target = File.Create("restored.tar"))
{
    await download!.CopyToAsync(target);
}
```

### Handling errors

```csharp
try
{
    await client.CreateContainerAsync("photos");
}
catch (PepperXException ex) when (ex.ErrorType == ApiErrorEnum.Conflict)
{
    // The container already exists.
}
```

Reads of missing resources return `null` rather than throwing: `ReadContainerAsync`,
`ReadObjectAsync`, `ReadObjectMetadataAsync`, and `OpenObjectAsync`.

## WebSocket quickstart

Reach for this when you are issuing many operations and want to avoid per-request connection
overhead. Requests are correlated by identifier, so concurrent calls on one client are safe.

```csharp
await using (PepperXWebsocketClient client = new PepperXWebsocketClient("ws://localhost:8002/"))
{
    await client.ConnectAsync();

    await client.CreateContainerAsync("events");
    await client.WriteObjectAsync("events", "e-1", Encoding.UTF8.GetBytes("payload"));

    byte[]? payload = await client.ReadObjectAsync("events", "e-1");
}
```

## Other protocols

PepperX also speaks S3, Redis RESP, and MCP. Those surfaces are best consumed with their standard
clients — `AWSSDK.S3`, `StackExchange.Redis`, and any MCP client — rather than through this SDK.
See the protocol references in the [repository root](../../README.md).

## Running the tests and the walkthrough

Both need a running node:

```bash
export PEPPERX_URL=http://localhost:8000
export PEPPERX_WS_URL=ws://localhost:8002/

dotnet run --project src/Test.Automated     # Touchstone suites
dotnet run --project src/Sdk.ConsoleApp     # narrated end-to-end walkthrough
```

The test suites skip rather than fail when no node is reachable.
