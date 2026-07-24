#!/usr/bin/env bash
#
# Factory reset: destroy the stack, its database, and its extents, then bring it back up empty and
# optionally seed it.
#
# This deletes every object in the deployment. It is meant for development and demos.
#
#   ./reset.sh          reset and seed with sample data
#   ./reset.sh --empty  reset and leave it empty
#
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
SEED=1

for arg in "$@"; do
  case "$arg" in
    --empty) SEED=0 ;;
    -h|--help) sed -n '2,12p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 1 ;;
  esac
done

cd "$COMPOSE_DIR"

echo "==> Stopping the stack and removing its volumes"
# -v is what makes this a factory reset rather than a restart: without it the Postgres data and the
# extent files survive, and the node would come back up describing objects you meant to destroy.
docker compose down -v --remove-orphans

echo "==> Starting a clean stack"
docker compose up -d --build

echo "==> Waiting for node1 to answer"
for _ in $(seq 1 60); do
  if curl --fail --silent --output /dev/null http://localhost:8000/v1.0/api/health; then
    echo "    node1 is healthy"
    break
  fi
  sleep 2
done

if ! curl --fail --silent --output /dev/null http://localhost:8000/v1.0/api/health; then
  echo "node1 did not become healthy. Check: docker compose logs node1" >&2
  exit 1
fi

if [ "$SEED" -eq 1 ]; then
  echo "==> Seeding sample data"
  python3 "${SCRIPT_DIR}/seed.py" http://localhost:8000
fi

echo
echo "Ready."
echo "  Dashboard  http://localhost:3000  (connect to http://localhost:8000)"
echo "  node1 REST http://localhost:8000    node2 REST http://localhost:8010"
echo "  S3         http://localhost:8001    RESP  localhost:6379"
