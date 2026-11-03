# Multi-stage build for the Roslyn MCP Server
# Provides container-based isolation for untrusted workspaces (MSBuild evaluation risk).

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY RoslynMcp.slnx Directory.Build.props Directory.Packages.props global.json ./
COPY src/Directory.Build.props src/
COPY src/RoslynMcp.Core/RoslynMcp.Core.csproj src/RoslynMcp.Core/
COPY src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj src/RoslynMcp.Roslyn/
COPY src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj src/RoslynMcp.Host.Stdio/
COPY analyzers/ServerSurfaceCatalogAnalyzer/ServerSurfaceCatalogAnalyzer.csproj analyzers/ServerSurfaceCatalogAnalyzer/
COPY tests/GitFixtureWorkerProbe/GitFixtureWorkerProbe.csproj tests/GitFixtureWorkerProbe/
COPY tests/ProcessPipeWorkerProbe/ProcessPipeWorkerProbe.csproj tests/ProcessPipeWorkerProbe/
COPY tests/RoslynMcp.ShardDiscoveryFixtures/RoslynMcp.ShardDiscoveryFixtures.csproj tests/RoslynMcp.ShardDiscoveryFixtures/
COPY tests/RoslynMcp.Tests/RoslynMcp.Tests.csproj tests/RoslynMcp.Tests/

RUN dotnet restore RoslynMcp.slnx

# Build the restored solution from the complete source graph, then publish the host.
COPY src/ src/
COPY analyzers/ analyzers/
COPY tests/ tests/
COPY BannedSymbols.txt ./
RUN dotnet build RoslynMcp.slnx -c Release --no-restore
RUN dotnet publish src/RoslynMcp.Host.Stdio -c Release -o /app --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime

# The full SDK is required at runtime because MSBuildWorkspace uses MSBuild for project evaluation.
# A smaller runtime-only image cannot load .sln/.csproj files.

# Run as non-root user for security hardening
RUN groupadd --system mcpuser && \
    useradd --system --gid mcpuser --create-home mcpuser

WORKDIR /app
COPY --from=build /app .

# Set read-only filesystem hint (mount volumes for workspace data)
# Container should be run with: docker run --read-only --tmpdir /tmp -v /path/to/workspace:/workspace
ENV DOTNET_EnableDiagnostics=0

USER mcpuser

ENTRYPOINT ["dotnet", "RoslynMcp.Host.Stdio.dll"]
