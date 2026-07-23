I want to build a high-performance, scalable key-value store named "PepperX" with high performance metadata and data persistence and retrieval.  It will be a backend system to users' applications with no authentication; authentication and access will need to be gated by whatever application is using this system. 

Its attributes include:
- Metadata will live in three forms: 
  - Labels - a list of strings
  - Tags - a dictionary with string keys and string values
  - Object - a singular unstructured JSON object or array
- Metadata will be stored in two forms:
  - Within shared database tables, labels and tags will be stored
  - Within raw storage units (called extents), including labels, tags, and object
  - Persisting in these ways will:
    a) allow for fast search against labels and tags to filter down to specific candidate extents
    b) allow for full rehydration of a database based on the raw file contents

Users will access this storage platform from within their applications using the following protocols:
- Native REST API
- S3 API
- Redis RESP
- Websockets API
- MCP API

To reiterate, the system will be fully unauthenticated as a backend storage platform behind another service.

Expectations of the system are as follows:
- Storage of extents should be abstracted through an interface/implementation IExtentStorageDriver, with the first implementation being DiskExtentStorageDriver which uses the local filesystem.  Extents should be organized into an object type called a container, and should be organized as such within the database.
- Extents are immutable, parallel reads should be allowed, deletes should block incoming reads and wait until existing reads are complete before executing
- Database containing metadata and information needed to understand extent storage must be shared across nodes and use Postgres, though this should be implemented using the same interface/implementation pattern, perhaps IMetadataDatabaseDriver, with the first implementation being PostgresMetadataDatabaseDriver
- The metadata database must account for labels, tags, and extent storage metadata.  Metadata objects need not be stored in the database, but rather, in the extents themselves

The native REST API should be the most complete, including full enumeration and search capabilities following the EnumerationQuery<T>/EnumerationResult<T> pattern found in the requirements docs in c:\code\agents\requirements.  Include basic CRUD, search/enumeration, and existence checks.

The native S3 API should only support buckets, objects, and tags.

RESP should support the plethora of types supported by Redis RESP and primitive APIs including PING PONG HELLO GET SET and others

Websockets API should be equivalent of REST but over persistent Websockets connection

MCP API should be equivalent of REST but over JSON-RPC using HTTP or TCP as transports.  Use Voltaic (NuGet package) for this implementation

Every REST API needs to use the Watson7 facilities for OpenAPI and Swagger and be fully annotated.

The backend stack is C#/Watson7 following those same requirements in c:\code\agents\requirements and using similar paradigms found in the reference applications.

Server nodes should be stateless and scale-out, with the database being authoritative.  Effectively these server nodes are protocol handlers for a backend Postgresql-based metadata store and data store.

Testing will use the Touchstone NuGet package (you can see the source in c:\code\touchstone) with test cases in Test.Shared, console runner in Test.Automated (using Touchstone CLI wrapper), Test.Xunit (Touchstone Xunit wrapper), Test.Nunit (Touchstone Nunit wrapper).  Add these to the C# solution.  These must test every nook and cranny of the solution with a particular focus on protocol correctness and semantic correctness

Test.Performance which will stand up a mock node and database using docker and stress test performance with a variety/mix of workloads.  This should be a console-runnable app, added to the solution, that shows what it is doing and produces beautifully formatted results on performance and throughput

The frontend stack is React following those same requirements, providing administrators (not regular users!) with visibility into extent storage, capacity, metadata.  Do no skimp out on time spent iterating on the user experience and functionality.  It should be complete.  

The repository will also need:
- SDKs in C#, Python, Javascript.  Use named types, don't return response body strings, return objects.  Each requires tests and console apps to exercise the SDKs
- README.md CHANGELOG.md REST_API.md MCP_API.md RESP_API.md WEBSOCKETS_API.md LICENSE.md (MIT)
- Postman collection
- .gitignore .dockerignore
- docker/ and docker/factory/ (including reset.bat/reset.sh) with compose.yaml

Ask any questions necessary before writing an EXHAUSTIVE PLAN in PEPPERX_PLAN.md that 1) is actionable, a dev can annotate progress and completion 2) is guided by the reference applications and 3) enforces conformance to the code style and usability requirements set forth in c:\code\agents\requirements.  These requirements are non-negotiable.