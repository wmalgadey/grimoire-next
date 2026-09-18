# syntax=docker/dockerfile:1
#
# grimoire-hub: the .NET 10 hub, the Node 22 runner build, the instruction files and the built
# frontend, in one image (contracts/deployment.md, "Container contract"). Build from the repository
# root:
#
#   docker build -f deploy/hub.Dockerfile -t grimoire-hub .
#
# Multi-platform (linux/amd64, linux/arm64) with buildx:
#
#   docker buildx build --platform linux/amd64,linux/arm64 -f deploy/hub.Dockerfile -t grimoire-hub .
#
# Configuration only: no setting and no instruction text is baked in beyond the defaults the
# environment contract names, and every one of those is an environment variable an operator can set.

ARG NODE_IMAGE=node:22-bookworm-slim
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# ---- the frontend: static assets, architecture-independent, so built once on the build platform
FROM --platform=$BUILDPLATFORM ${NODE_IMAGE} AS frontend
WORKDIR /src
COPY contracts/ contracts/
COPY frontend/package.json frontend/package-lock.json frontend/
RUN npm --prefix frontend ci
COPY frontend/ frontend/
RUN npm --prefix frontend run build

# ---- the runner: built on the target platform, because the agent SDK's dependencies carry
# platform-specific binaries and the ones installed must be the ones that run
FROM ${NODE_IMAGE} AS runner
WORKDIR /src/src/agentrun
COPY src/agentrun/package.json src/agentrun/package-lock.json ./
RUN npm ci
COPY src/agentrun/ ./
RUN npm run build && npm prune --omit=dev

# ---- the hub: framework-dependent IL, architecture-independent, so built on the build platform
FROM --platform=$BUILDPLATFORM ${DOTNET_SDK_IMAGE} AS hub
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/hub/Grimoire.Hub.csproj --configuration Release --output /out/hub

# ---- the image
FROM ${DOTNET_RUNTIME_IMAGE}

# git is the wiki's only mutation path (src/wiki/); node runs the agent harness. Nothing else.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
COPY --from=runner /usr/local/bin/node /usr/local/bin/node

# The layout the hub resolves against (GRIMOIRE_ROOT): src/agentrun/dist/main.js and the default
# instruction path src/instructions/ingest.md, both relative to /app.
WORKDIR /app
COPY --from=hub /out/hub/ hub/
COPY --from=runner /src/src/agentrun/package.json src/agentrun/package.json
COPY --from=runner /src/src/agentrun/dist/ src/agentrun/dist/
COPY --from=runner /src/src/agentrun/node_modules/ src/agentrun/node_modules/
COPY src/instructions/ src/instructions/
COPY --from=frontend /src/frontend/build/ wwwroot/

# The two volume mount points, owned by the unprivileged user so a fresh named volume inherits a
# directory that user can write. The root filesystem is otherwise read-only at run time.
RUN mkdir -p /data/wiki /data/state && chown -R "$APP_UID" /data

ENV GRIMOIRE_ROOT=/app \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_WEBROOT=/app/wwwroot \
    GRIMOIRE_WIKI_REPO=/data/wiki \
    GRIMOIRE_STATE_DB=/data/state/grimoire.db \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1

# Non-root: the runtime image's own unprivileged user. No capabilities are added anywhere.
USER $APP_UID
EXPOSE 8080
VOLUME ["/data/wiki", "/data/state"]
ENTRYPOINT ["dotnet", "/app/hub/Grimoire.Hub.dll"]
