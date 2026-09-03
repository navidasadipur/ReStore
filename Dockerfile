# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# ---------- Stage 1: build React client ----------
FROM node:18-alpine AS client-build

WORKDIR /client

COPY client/package.json client/package-lock.json ./

RUN npm ci

COPY client/ ./

RUN npm run build

# ---------- Stage 2: publish API ----------
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS api-build

WORKDIR /src

COPY API/API.csproj API/

RUN dotnet restore "API/API.csproj"

COPY API/ API/

COPY --from=client-build /client/build API/wwwroot/

RUN dotnet publish "API/API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

# ---------- Stage 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final

WORKDIR /app

RUN groupadd --system appgroup \
    && useradd --system \
       --gid appgroup \
       --no-create-home \
       appuser

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=api-build --chown=appuser:appgroup /app/publish .

USER appuser

ENTRYPOINT ["dotnet", "API.dll"]