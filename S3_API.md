# PepperX S3 API

PepperX speaks enough of the S3 API that existing S3 tooling — the AWS CLI, the AWS SDKs, `s3cmd`,
`rclone` — works against it unchanged for the operations it supports. The point is not S3
compatibility for its own sake; it is that you do not have to write a PepperX client to use PepperX
from a language that already has an S3 one.

The surface is deliberately narrow: **buckets, objects, and tags**. Everything else returns
`NotImplemented`. See [what is not implemented](#what-is-not-implemented) for why.

Default port: **8001**.

---

## Contents

- [Mapping onto PepperX concepts](#mapping-onto-pepperx-concepts)
- [Authentication](#authentication)
- [Connecting](#connecting)
- [Supported operations](#supported-operations)
- [What is not implemented](#what-is-not-implemented)
- [Errors](#errors)

---

## Mapping onto PepperX concepts

| S3 | PepperX |
|---|---|
| Bucket | Container |
| Object key | Object key |
| Object body | Extent payload |
| Object tagging | Object tags |
| Bucket tagging | Container tags |
| ETag | MD5 of the payload |

Container names already follow S3 bucket naming rules — 3–63 characters, lowercase letters, digits,
and hyphens — so a container created over REST is immediately addressable as a bucket, and vice
versa. This is one namespace, not two: an object written over REST is readable over S3 with the same
key and the same bytes.

PepperX labels and the freeform metadata object have no S3 equivalent. They are preserved on objects
written over S3 (as empty and null respectively) and are fully visible over REST. If you need them,
write over REST or use tags, which S3 does model.

### ETag is MD5, not SHA-256

PepperX checksums payloads with SHA-256 and exposes that as `x-pepperx-sha256`. The S3 surface
additionally computes MD5 for the `ETag`, because the AWS SDKs validate `ETag` against their own MD5
of the uploaded bytes and reject the response otherwise. Treat `ETag` as an S3 protocol detail;
`x-pepperx-sha256` over REST is the real integrity check.

---

## Authentication

By default the S3 surface accepts **anonymous** requests (`S3.AllowAnonymous: true`), consistent with
PepperX being unauthenticated infrastructure.

Static credentials exist (`S3.StaticAccessKey` / `S3.StaticSecretKey`, both `pepperx` by default) but
only because most S3 clients refuse to send an unsigned request — the AWS SDKs in particular will not
construct one. They are a client-compatibility affordance, **not** a security control: signatures are
accepted rather than verified against a credential store, and anonymous access remains enabled. Do
not treat configuring them as access control.

---

## Connecting

Every client needs path-style addressing. Virtual-host style (`bucket.host`) requires DNS entries per
bucket, which is not something a self-hosted node can arrange.

### AWS CLI

```bash
aws configure set aws_access_key_id pepperx
aws configure set aws_secret_access_key pepperx
aws configure set region us-west-1

alias pxs3='aws --endpoint-url http://localhost:8001 s3'

pxs3 mb s3://telemetry
pxs3 cp ./cpu.json s3://telemetry/metrics/cpu.json
pxs3 ls s3://telemetry/
pxs3 cp s3://telemetry/metrics/cpu.json ./downloaded.json
pxs3 rm s3://telemetry/metrics/cpu.json
```

### boto3

```python
import boto3
from botocore.config import Config

s3 = boto3.client(
    "s3",
    endpoint_url="http://localhost:8001",
    aws_access_key_id="pepperx",
    aws_secret_access_key="pepperx",
    region_name="us-west-1",
    # Path-style is required; the default virtual-host style would resolve
    # telemetry.localhost, which does not exist.
    config=Config(s3={"addressing_style": "path"}),
)

s3.create_bucket(Bucket="telemetry")
s3.put_object(
    Bucket="telemetry",
    Key="metrics/cpu.json",
    Body=b'{"cpu":0.42}',
    ContentType="application/json",
    Tagging="resolution=1m&host=web-01",
)
print(s3.get_object(Bucket="telemetry", Key="metrics/cpu.json")["Body"].read())
```

### AWS SDK for .NET

```csharp
AmazonS3Config config = new AmazonS3Config
{
    ServiceURL = "http://localhost:8001",
    ForcePathStyle = true,
    AuthenticationRegion = "us-west-1"
};

using AmazonS3Client client = new AmazonS3Client("pepperx", "pepperx", config);
await client.PutBucketAsync("telemetry");
```

---

## Supported operations

### Service

| Operation | Notes |
|---|---|
| `ListBuckets` | All containers |

### Buckets

| Operation | Notes |
|---|---|
| `CreateBucket` | Subject to container naming rules |
| `HeadBucket` | Existence check |
| `ListObjects` / `ListObjectsV2` | Supports `prefix`, `delimiter`, `max-keys`, continuation |
| `GetBucketLocation` | Returns the configured `S3.Region` |
| `DeleteBucket` | Fails if the bucket is not empty, matching S3 |
| `GetBucketTagging` / `PutBucketTagging` / `DeleteBucketTagging` | Container tags |

### Objects

| Operation | Notes |
|---|---|
| `PutObject` | Streams the body; supports AWS chunked/streaming signatures |
| `GetObject` | Supports `Range` |
| `HeadObject` | Metadata without the payload |
| `DeleteObject` | |
| `DeleteObjects` | Batch delete |
| `GetObjectTagging` / `PutObjectTagging` / `DeleteObjectTagging` | Object tags |

`PutObject` accepts the AWS streaming-signature body format (`STREAMING-AWS4-HMAC-SHA256-PAYLOAD`),
which the SDKs use by default for uploads. The chunk framing is decoded before the payload is stored,
so an object uploaded by an SDK is byte-identical to the same object uploaded by `curl` over REST.

### Multipart upload

| Operation | Notes |
|---|---|
| `CreateMultipartUpload` | Returns an opaque `UploadId` |
| `UploadPart` | Per-part ETag is the part's MD5 |
| `UploadPartCopy` | Copies a part from an existing object; honors `x-amz-copy-source-range` |
| `CompleteMultipartUpload` | Assembles the parts into one object |
| `AbortMultipartUpload` | Discards staged parts |
| `ListParts` | Paginated (`max-parts`, `part-number-marker`) |
| `ListMultipartUploads` | Paginated (`max-uploads`, `key-marker`, `upload-id-marker`) |

The AWS CLI and every AWS SDK switch to multipart automatically once an upload crosses their threshold
(8 MiB for `aws s3 cp` by default), so uploading a large object "just works":

```bash
aws --endpoint-url http://localhost:8001 s3 cp ./4gb.bin s3://telemetry/4gb.bin
```

Semantics worth knowing:

- **Parts stage on shared storage** and are assembled into a single immutable object when the upload
  completes. Because completion runs through the ordinary write path, a multipart-assembled object is
  indistinguishable from one written in a single `PutObject` — it reads, caches, and rebuilds the same way.
- **Part size.** Every part except the last must be at least 5 MiB (`S3.MultipartMinPartBytes`, matching
  S3). An upload may have up to 10,000 parts (`S3.MultipartMaxParts`).
- **ETag.** The per-part ETag is the part's MD5. The completed object's ETag is
  `hex(MD5(concatenation of the raw part MD5s)) + "-" + partCount` — the standard S3 multipart form —
  and it is returned on `GetObject`, `HeadObject`, and `ListObjects` alike.
- **Multi-node.** Parts and their metadata are shared cluster-wide, so a client may upload parts against
  one node and complete against another.
- **Expiry.** An upload that is never completed or aborted is reclaimed after `S3.MultipartUploadExpiryDays`
  (default 7). A full `rehydrate --mode Rebuild` abandons in-flight uploads (they have no object yet);
  their staged parts are then reclaimed by the janitor.
- Cross-node in-progress uploads are also visible over REST at
  `GET /v1.0/containers/{container}/multipart-uploads` (see [`REST_API.md`](REST_API.md)).

**Downloading large objects.** `GetObject` returns the whole object, and a `Range` request returns the
requested bytes with `206 Partial Content` and a `Content-Range: bytes start-end/total` header that
carries the full object size. Large-object downloads that use ranged/multipart transfer — `aws s3 cp`,
the AWS SDKs, and `mc cp` — work byte-for-byte, so upload and download both "just work" with the CLI.

### A note on ETags

An object's S3 ETag is derived from its **MD5**: a plain MD5 hex for a single-`PutObject` object, and the
`…-N` multipart form for a completed multipart object. PepperX also records each object's SHA-256 (its
native content hash), surfaced over REST as the `md5` and `sha256` fields on object metadata. `GetObject`,
`HeadObject`, and `ListObjects` all report the same MD5-based ETag for a given object.

---

## What is not implemented

Returns `NotImplemented`:

- **Versioning** — every write creates a new immutable extent, but only the current one is
  addressable by key. There is no version ID to expose.
- **ACLs and bucket policies** — there is no authentication to attach permissions to. An ACL that
  cannot be enforced is worse than an absent one.
- **Object lock, retention, and legal hold** — no compliance mode.
- **Lifecycle rules, replication, inventory, analytics**
- **Server-side encryption** — encrypt the volume underneath.
- **Website hosting, CORS configuration, request payment, logging, notifications**
- **S3 Select**

If you need something here, the REST API is the more complete surface — this list is what S3 models
that PepperX does not, rather than a list of things PepperX cannot do.

---

## Errors

Errors are returned as S3 XML with the conventional codes, so S3 clients handle them normally:

| PepperX condition | S3 code | HTTP |
|---|---|---|
| Container not found | `NoSuchBucket` | 404 |
| Object not found | `NoSuchKey` | 404 |
| Bucket already exists | `BucketAlreadyExists` | 409 |
| Bucket not empty | `BucketNotEmpty` | 409 |
| Payload too large | `EntityTooLarge` | 413 |
| Malformed request | `InvalidRequest` | 400 |
| Unknown/expired multipart upload | `NoSuchUpload` | 404 |
| Part missing or ETag mismatch on complete | `InvalidPart` | 400 |
| Parts not in ascending order | `InvalidPartOrder` | 400 |
| Non-final part below the minimum size | `EntityTooSmall` | 400 |
| Unsupported operation | `NotImplemented` | 501 |

---

## See also

- [`REST_API.md`](REST_API.md) — the complete surface, including labels and metadata objects
- [`RESP_API.md`](RESP_API.md) · [`WEBSOCKETS_API.md`](WEBSOCKETS_API.md) · [`MCP_API.md`](MCP_API.md)
