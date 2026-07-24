# PepperX server node.
#
# Build from the repository root so the build context includes src/:
#   docker build -f docker/server/Dockerfile -t pepperx-server .
#
# The image ships no settings file of its own. A node reads ./pepperx.json from its working
# directory, so mount one at /app/pepperx.json -- baking configuration into the image would mean
# rebuilding to change a port, and would put a database password in an image layer.

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build

WORKDIR /build

# Project files first: restore is the slow step and only needs to rerun when dependencies change.
COPY src/Directory.Build.props ./src/
COPY src/PepperX.Core/PepperX.Core.csproj ./src/PepperX.Core/
COPY src/PepperX.Server/PepperX.Server.csproj ./src/PepperX.Server/
COPY pepperx.json ./

# The projects multi-target net8.0 and net10.0; the image only needs one, and restoring both would
# pull a framework this base image does not carry.
RUN dotnet restore src/PepperX.Server/PepperX.Server.csproj -p:TargetFramework=net8.0

COPY src/PepperX.Core/ ./src/PepperX.Core/
COPY src/PepperX.Server/ ./src/PepperX.Server/

RUN dotnet publish src/PepperX.Server/PepperX.Server.csproj \
    -c Release \
    -f net8.0 \
    --no-restore \
    -o /publish

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime

LABEL org.opencontainers.image.title="PepperX" \
      org.opencontainers.image.description="High-performance key-value store with rich metadata, immutable extents, and dual persistence" \
      org.opencontainers.image.licenses="MIT"

# curl is used by the healthcheck below and is genuinely useful when debugging a node from inside
# its own container.
RUN apk add --no-cache curl icu-libs

# Globalization stays on: object keys, container names, and metadata are user data in any language,
# and invariant mode would change how they compare and sort.
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /publish ./

# Extents live here. Mount a volume -- without one, deleting the container destroys the objects, and
# the metadata database would then describe extents that no longer exist.
RUN mkdir -p /app/data/extents

# Run unprivileged. Nothing the server does needs root, and the storage directory is the only path
# it writes to.
RUN addgroup -S pepperx && adduser -S pepperx -G pepperx && chown -R pepperx:pepperx /app
USER pepperx

# REST, S3, WebSockets, MCP (HTTP), MCP (TCP), RESP. Ports come from the mounted settings file; these
# are the defaults, and EXPOSE is documentation rather than enforcement.
EXPOSE 8000 8001 8002 8003 8004 6379

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8000/v1.0/api/health || exit 1

ENTRYPOINT ["dotnet", "PepperX.Server.dll"]
