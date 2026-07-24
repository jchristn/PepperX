# Node configuration

`node1.json` and `node2.json` are mounted read-only into the two nodes in `compose.yaml`. They are
identical except that neither pins a `Cluster.NodeId` — a node generates one at startup and registers
it in the database, so two nodes from the same file still appear as two distinct cluster members.

Both differ from the repository's root `pepperx.json` in two ways:

- `Database.Hostname` is `postgres`, the compose service name, rather than `localhost`.
- `Logging.FileLogging` is off. Container logs belong on stdout where `docker logs` and any log
  collector can see them; writing to a file inside a container just hides them.

## Changing configuration

Edit the file and restart that node:

```bash
docker compose restart node1
```

Settings are read once at startup. There is no reload endpoint, which is why the dashboard presents
them read-only.

## What not to put here

These files are checked into the repository, so the credentials in them are public knowledge. For a real
deployment, keep your own settings file outside version control and mount that instead:

```yaml
volumes:
  - /etc/pepperx/production.json:/app/pepperx.json:ro
```

The database password and the S3 static keys are the two values worth protecting. Everything else is
topology.
