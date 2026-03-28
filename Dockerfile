# Multi-stage build for the Roslyn MCP Server
# Provides container-based isolation for untrusted workspaces (MSBuild evaluation risk).

# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY RoslynMcp.slnx Directory.Build.props Directory.Packages.props global.json ./
COPY src/RoslynMcp.Core/RoslynMcp.Core.csproj src/RoslynMcp.Core/
COPY src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj src/RoslynMcp.Roslyn/
COPY src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj src/RoslynMcp.Host.Stdio/

RUN dotnet restore RoslynMcp.slnx

# Copy remaining source and publish
COPY src/ src/
RUN dotnet publish src/RoslynMcp.Host.Stdio -c Release -o /app --no-restore

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS runtime

# The full SDK is required at runtime because MSBuildWorkspace uses MSBuild for project evaluation.
# A smaller runtime-only image cannot load .sln/.csproj files.

# Run as non-root user for security hardening
RUN groupadd --gid 1000 mcpuser && \
    useradd --uid 1000 --gid mcpuser --create-home mcpuser

WORKDIR /app
COPY --from=build /app .

# Set read-only filesystem hint (mount volumes for workspace data)
# Container should be run with: docker run --read-only --tmpdir /tmp -v /path/to/workspace:/workspace
ENV DOTNET_EnableDiagnostics=0

USER mcpuser

ENTRYPOINT ["dotnet", "RoslynMcp.Host.Stdio.dll"]
