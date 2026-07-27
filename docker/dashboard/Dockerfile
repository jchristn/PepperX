# PepperX admin dashboard.
#
# The dashboard is a static single-page app that talks to a node from the browser, so the container
# serves files and nothing else -- there is no server-side rendering and no backend proxy. The node's
# address is entered on the connect screen at runtime rather than baked in at build time, which is
# what lets one image point at any node.
#
# Build from the repository root:
#   docker build -f docker/dashboard/Dockerfile -t pepperx-dashboard .

FROM node:22-alpine AS build

WORKDIR /build

# Copy manifests first so a source-only change does not invalidate the dependency layer.
COPY dashboard/package.json dashboard/package-lock.json* ./
RUN npm ci --no-audit --no-fund

COPY dashboard/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime

LABEL org.opencontainers.image.title="PepperX Dashboard" \
      org.opencontainers.image.description="Administrative console for PepperX nodes" \
      org.opencontainers.image.licenses="MIT"

COPY --from=build /build/dist /usr/share/nginx/html
COPY docker/dashboard/nginx.conf /etc/nginx/conf.d/default.conf
COPY docker/dashboard/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Default server URL offered on the connect screen. Override per deployment:
#   docker run -e PEPPERX_SERVER_URL=http://node1.internal:8000 ...
# The entrypoint writes it into the web root at start, so one image serves any node.
ENV PEPPERX_SERVER_URL=http://127.0.0.1:8000

EXPOSE 80

# 127.0.0.1, not localhost: nginx's `listen 80` binds IPv4 only, while busybox wget resolves
# localhost to ::1 first and gives up on the refusal rather than falling back to IPv4. The container
# then reports unhealthy while serving pages perfectly well. (curl does happy-eyeballs and would have
# hidden this, which is why the server image's healthcheck was unaffected.)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://127.0.0.1/ || exit 1

ENTRYPOINT ["/entrypoint.sh"]
