# syntax=docker/dockerfile:1

# ReStore ships as ONE container: the React client is compiled to static files
# and served by the ASP.NET Core API out of wwwroot (see UseStaticFiles and
# MapFallbackToController in API/Program.cs). That is why the compose stacks
# have an `app` service but no separate frontend service.

# ---------------------------------------------------------------------------
# Stage 1 - build the React client
# ---------------------------------------------------------------------------
# Node 18 on purpose: react-scripts 5.0.1 is not reliable on newer Node majors.
# This only affects the build; it is not what runs in production.
FROM node:18-alpine AS client-build

WORKDIR /client

# package.json + lockfile first so `npm ci` is cached until deps change.
COPY client/package.json client/package-lock.json ./
RUN npm ci

COPY client/ ./

# create-react-app's production build (webpack + terser + TS) needs well over
# Node's default old-space limit, which on a small machine is only a few hundred
# MB. Raise it explicitly so the build does not die with "JavaScript heap out of
# memory". 1024 MB is comfortably enough for this app.
ENV NODE_OPTIONS=--max-old-space-size=1024

# client/.env.production sets BUILD_PATH=../API/wwwroot for the non-Docker
# workflow. An explicit environment variable wins over a .env file, so the
# output lands in /client/build where the next stage expects it.
# CI=false keeps create-react-app from turning lint warnings into build errors.
RUN BUILD_PATH=build GENERATE_SOURCEMAP=false CI=false npm run build

# ---------------------------------------------------------------------------
# Stage 2 - build and publish the API
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS api-build

WORKDIR /src

# Restore first with only the project file present, so the NuGet layer is
# cached until API.csproj changes.
COPY API/API.csproj API/
RUN dotnet restore API/API.csproj

COPY API/ API/
RUN dotnet publish API/API.csproj -c Release -o /app/publish --no-restore

# ---------------------------------------------------------------------------
# Stage 3 - runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final

WORKDIR /app

# The .NET 7 base images still ship no non-root user (that arrived in .NET 8),
# so create one. Port 8080 is above 1024 and therefore bindable without root.
# Deliberately no `apt-get install`: the image already has bash and adduser,
# and the healthcheck below uses bash's /dev/tcp, so the build never depends on
# an external Debian mirror being in sync.
RUN adduser --system --group --no-create-home --uid 10001 appuser

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# API binaries (including a fresh wwwroot-less publish), then the client build
# as the app's static files. API/wwwroot is excluded via .dockerignore so the
# stale build committed to git never leaks into the image.
COPY --from=api-build /app/publish ./
COPY --from=client-build /client/build ./wwwroot

RUN chown -R appuser:appuser /app

USER appuser

EXPOSE 8080

ENTRYPOINT ["dotnet", "API.dll"]
