# Production frontend served by nginx from a Container App (the production subscription cannot host
# Static Web Apps). Context is the repository root; only frontend/ is copied.
# VITE_GATEWAY_URL is compiled into the bundle, so CD passes the deployed gateway origin; the
# localhost default only serves image builds and scans in CI. RELEASE_SHA is published in
# /version.json, where the release planner reads the commit the site runs.
FROM node:22-alpine AS build
WORKDIR /source/frontend

COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
ARG VITE_GATEWAY_URL=http://localhost:8000
ARG RELEASE_SHA=unknown
RUN VITE_GATEWAY_URL="$VITE_GATEWAY_URL" npm run build \
    && printf '{"sha":"%s"}\n' "$RELEASE_SHA" > dist/version.json

FROM nginxinc/nginx-unprivileged:1.28-alpine AS runtime

# curl is not in the base image and the HEALTHCHECK below needs it.
# The upgrade applies OS security fixes the base image has not picked up yet.
USER root
RUN apk upgrade --no-cache \
    && apk add --no-cache curl

COPY frontend/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY frontend/nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /source/frontend/dist /usr/share/nginx/html

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --retries=5 --start-period=5s \
    CMD curl --fail --silent http://localhost:8080/healthz || exit 1

# The unprivileged image's nginx user; nginx listens on 8080 and writes only to /tmp.
USER 101
