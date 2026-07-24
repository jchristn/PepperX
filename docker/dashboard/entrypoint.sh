#!/bin/sh
set -e

# Inject runtime configuration into the static bundle.
#
# The dashboard is a static SPA: by the time a browser runs it, environment variables are long gone.
# The usual alternative -- baking the URL in at build time -- would mean one image per deployment,
# which defeats the point of a console that can point at any node. So the value is written to a file
# in the web root at container start and fetched by the app before it renders.
#
# Deliberately not a template substitution over index.html: the bundle's asset hashes change every
# build, and rewriting hashed files is a good way to break integrity checks later.

SERVER_URL="${PEPPERX_SERVER_URL:-http://localhost:8000}"

cat > /usr/share/nginx/html/config.json <<EOF
{
  "defaultServerUrl": "${SERVER_URL}"
}
EOF

echo "PepperX dashboard: default server URL is ${SERVER_URL}"

exec nginx -g 'daemon off;'
