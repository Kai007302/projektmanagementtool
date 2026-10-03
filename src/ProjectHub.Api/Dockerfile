# ProjectHub API (ADR 0012). Build from the repository root, the migrations are embedded:
# docker build -f src/ProjectHub.Api/Dockerfile -t projecthub-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ProjectHub.Api/ProjectHub.Api.csproj src/ProjectHub.Api/
RUN dotnet restore src/ProjectHub.Api/ProjectHub.Api.csproj
COPY database/migrations database/migrations
COPY src/ProjectHub.Api src/ProjectHub.Api
RUN dotnet publish src/ProjectHub.Api/ProjectHub.Api.csproj --configuration Release --no-restore --output /app \
    && mkdir -p /data/attachments /data/whiteboards

# Chiseled: no shell, no package manager, runs as the non-root user "app". "extra" adds ICU and time zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=app:app /data /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    PROJECTHUB_ATTACHMENT_DIRECTORY=/data/attachments \
    PROJECTHUB_WHITEBOARD_DIRECTORY=/data/whiteboards
EXPOSE 8080
# The apphost, so the whiteboard engine (ADR 0009) can start this executable again as its child process.
ENTRYPOINT ["./ProjectHub.Api"]
