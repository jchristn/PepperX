# PepperX S3 API

> Stub — the full reference (supported/unsupported operation matrix, anonymous and
> static-credential configuration, AWS CLI/SDK examples, error mapping) is written
> in Phase 15. See `PEPPERX_PLAN.md` §11.3.

PepperX exposes an S3-compatible surface limited to **buckets, objects, and tags**.
A bucket maps to a container; an object key maps to an extent key. Multipart upload,
ACLs, versioning, retention, select, website, and logging are intentionally not
implemented and return `NotImplemented`.
