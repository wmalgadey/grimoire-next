# syntax=docker/dockerfile:1
#
# grimoire-egress: the YARP proxy in src/egress/ — the only route out of the hub container and the
# only holder of the upstream credential (ADR-0010, ADR-0012). Build from the repository root:
#
#   docker build -f deploy/egress.Dockerfile -t grimoire-egress .
#
# The credential is configuration (GRIMOIRE_EGRESS_MODEL_CREDENTIAL), never a layer of this image.

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM --platform=$BUILDPLATFORM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/egress/ src/egress/
RUN dotnet publish src/egress/Grimoire.Egress.csproj --configuration Release --output /out

FROM ${DOTNET_RUNTIME_IMAGE}
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=1
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "/app/Grimoire.Egress.dll"]
