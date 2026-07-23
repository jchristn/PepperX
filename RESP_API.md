# PepperX RESP API

> Stub — the full command reference (semantics, unsupported options, RESP2/RESP3
> notes, redis-cli and StackExchange.Redis examples) is written in Phase 15. See
> `PEPPERX_PLAN.md` §11.4.

PepperX speaks the Redis Serialization Protocol (RESP2 and RESP3) on port 6379.
Each RESP database index maps to a container (`resp0`…`respN`). Supported commands
include PING, ECHO, HELLO, SELECT, GET, SET (NX/XX), SETNX, GETSET, GETDEL, MGET,
MSET, DEL, EXISTS, STRLEN, TYPE, KEYS, SCAN, DBSIZE, FLUSHDB, and the INCR family.
Expiry options are not supported.
