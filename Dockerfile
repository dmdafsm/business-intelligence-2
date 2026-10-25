# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
WORKDIR /source
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    NUGET_PACKAGES=/opt/nuget/packages

COPY global.json NuGet.Config Directory.Build.props Directory.Packages.props ./
COPY src/BusinessIntelligence.Host/BusinessIntelligence.Host.csproj src/BusinessIntelligence.Host/packages.lock.json ./src/BusinessIntelligence.Host/
COPY src/BusinessIntelligence.Modules/BusinessIntelligence.Modules.csproj src/BusinessIntelligence.Modules/packages.lock.json ./src/BusinessIntelligence.Modules/
RUN dotnet restore src/BusinessIntelligence.Host/BusinessIntelligence.Host.csproj --locked-mode -warnaserror
COPY . .
RUN dotnet publish src/BusinessIntelligence.Host/BusinessIntelligence.Host.csproj \
    --configuration Release --no-restore --output /publish -warnaserror -p:UseAppHost=false

FROM build AS verify
ARG TARGETARCH
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends python3 postgresql-16 git \
    && rm -rf /var/lib/apt/lists/*
RUN python3 scripts/install_powershell.py "$TARGETARCH" /opt/powershell
ENV PATH="/opt/powershell:${PATH}" \
    PLAYWRIGHT_BROWSERS_PATH=/opt/playwright \
    BI_BROWSER_SYSTEM_DEPS=preinstalled
RUN dotnet restore BusinessIntelligence.slnx --locked-mode -warnaserror \
    && dotnet build BusinessIntelligence.slnx --configuration Release --no-restore -warnaserror \
    && pwsh tests/BusinessIntelligence.BrowserTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
RUN git init --initial-branch=main \
    && git add --all \
    && mkdir -p /source/.artifacts \
    && chown -R app:app /source /opt/nuget /opt/playwright
ENV BI_TEST_POSTGRES_BIN=/usr/lib/postgresql/16/bin \
    DOTNET_CLI_HOME=/tmp/dotnet
USER app
CMD ["python3", "scripts/verify.py"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble-chiseled-extra@sha256:3ed6558056b54d5c9dfefb7ff473728e022ff2bf1d976cbf8ee07b05ec594374 AS runtime
WORKDIR /app
COPY --from=build /publish/ ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=5 \
    CMD ["dotnet", "BusinessIntelligence.Host.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "BusinessIntelligence.Host.dll"]
