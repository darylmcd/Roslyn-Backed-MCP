# Roslyn-Backed MCP Server — semantic C# analysis for AI coding agents
# Requires: https://github.com/casey/just — plus .NET 10 SDK, Docker (optional)

# Variables
solution := "RoslynMcp.slnx"
host-project := "src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj"
nupkg-dir := "nupkg"

# Cross-platform shell
set windows-shell := ["pwsh.exe", "-NoProfile", "-Command"]
set shell := ["sh", "-cu"]

# Show available recipes
default:
    @just --list

# --- Build ---

# Build the solution (Debug)
build:
    dotnet build {{ solution }} --nologo

# Build the solution (Release)
build-release:
    dotnet build {{ solution }} -c Release --nologo

# Restore NuGet packages
restore:
    dotnet restore {{ solution }} --nologo

# --- Test ---

# Run all tests (Debug)
test:
    dotnet test {{ solution }} --nologo

# Run all tests (Release)
test-release:
    dotnet test {{ solution }} -c Release --nologo

# --- Lint / Validation ---

# Validate AI documentation structure
verify-docs:
    ./eng/verify-ai-docs.ps1

# Check version-string drift across all 5 version files
verify-version-drift:
    ./eng/verify-version-drift.ps1

# Check shipped skills (./skills/) have no repo-specific references
verify-skills:
    ./eng/verify-skills-are-generic.ps1

# --- Run ---

# Run the stdio host process locally
run:
    dotnet run --project {{ host-project }}

# --- Aggregates ---

# Fast local sanity check before pushing (build + test)
validate: build test

# Local equivalent of the CI pipeline (docs + skills + release validation + vuln audit)
ci: verify-docs verify-skills verify-release vuln-audit

# Everything including full release validation
full: verify-docs verify-skills verify-release vuln-audit

# --- Clean ---

# Clean build outputs
clean:
    dotnet clean {{ solution }} --nologo

# Clean build outputs and artifacts directory
clean-all: clean
    rm -rf artifacts

# --- Packaging ---

# Pack the global tool NuGet package
pack:
    dotnet pack {{ host-project }} -c Release -o {{ nupkg-dir }}

# Update or install global `roslynmcp` from nuget.org (package id Darylmcd.RoslynMcp)
tool-update:
    dotnet tool update -g Darylmcd.RoslynMcp || dotnet tool install -g Darylmcd.RoslynMcp
    dotnet tool list -g

# Pack Release nupkg, then install that build as the global tool (maintainer / dogfood).
# Uninstalls legacy package id RoslynMcp if present, then installs Darylmcd.RoslynMcp from nupkg/.
# Windows: one line so Version is read in the same pwsh invocation as install (each recipe line is a new shell).
# Windows also ends roslynmcp.exe first so the global tool folder is not locked (same idea as ReinstallTool in the csproj).
[unix]
tool-install-local: pack
    ver="$(dotnet msbuild -nologo {{ host-project }} -getProperty:Version)"
    dotnet tool uninstall -g Darylmcd.RoslynMcp || true
    dotnet tool uninstall -g RoslynMcp || true
    dotnet tool install -g Darylmcd.RoslynMcp --add-source "{{ nupkg-dir }}" --version "$ver"

[windows]
tool-install-local: pack
    $ver = (dotnet msbuild -nologo {{ host-project }} -getProperty:Version).Trim(); cmd /c "taskkill /F /IM roslynmcp.exe 1>nul 2>nul & dotnet tool uninstall -g Darylmcd.RoslynMcp 1>nul 2>nul & dotnet tool uninstall -g RoslynMcp 1>nul 2>nul & dotnet tool install -g Darylmcd.RoslynMcp --add-source {{ nupkg-dir }} --version $ver"

# Reload the Claude Code plugin from the local repo (Layer 2)
plugin-reload:
    pwsh ./eng/update-claude-plugin.ps1

# Full reinstall: Layer 1 (global tool) + Layer 2 (Claude Code plugin)
reinstall: tool-install-local plugin-reload

# Publish the host project (Release)
publish-host:
    dotnet publish {{ host-project }} -c Release -o artifacts/publish/host-stdio

# Build the Docker image
docker-build:
    docker build -t roslynmcp .

# --- Security / Audit ---

# Audit NuGet packages for known vulnerabilities
vuln-audit:
    dotnet package list --project {{ solution }} --vulnerable --include-transitive

# --- Repo Hygiene ---

# Run the full release verification pipeline (restore, build, test with coverage, publish, hash manifest)
verify-release:
    ./eng/verify-release.ps1
