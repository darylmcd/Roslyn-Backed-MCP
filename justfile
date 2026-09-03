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

# Restore every owned sample fixture used by integration tests
prepare-test-fixtures:
    pwsh -NoProfile -File ./eng/prepare-test-fixtures.ps1

# Run all tests (Debug)
test: prepare-test-fixtures
    dotnet test {{ solution }} --nologo -p:TestFixturesPrepared=true

# Run all tests (Release)
test-release: prepare-test-fixtures
    dotnet test {{ solution }} -c Release --nologo -p:TestFixturesPrepared=true

# --- Lint / Validation ---

# Validate AI documentation structure
verify-docs:
    pwsh -NoProfile -File ./eng/verify-ai-docs.ps1

# Check version-string drift across all six version files
verify-version-drift:
    pwsh -NoProfile -File ./eng/verify-version-drift.ps1

# Check shipped skills (./skills/) have no repo-specific references
verify-skills:
    pwsh -NoProfile -File ./eng/verify-skills-are-generic.ps1

# Gate formatter debt introduced on the C# files this change touches (baseline debt stays tracked)
verify-changed-format:
    pwsh -NoProfile -File ./eng/verify-changed-format.ps1

# MCP registry install-readiness scorecard (writes artifacts/registry-readiness.json)
verify-registry-readiness:
    pwsh -NoProfile -File ./eng/verify-registry-readiness.ps1

# --- Run ---

# Run the stdio host process locally
run:
    dotnet run --project {{ host-project }}

# --- Aggregates ---

# Fast local sanity check before pushing (build + test)
validate: build test

# Local equivalent of the required pull-request pipeline (no coverage/live-network canary)
ci: verify-docs verify-skills verify-changed-format verify-release-pr vuln-audit

# Everything including coverage and the live-network canary
full: verify-docs verify-skills verify-changed-format verify-release vuln-audit

# --- Clean ---

# Clean build outputs
clean:
    dotnet clean {{ solution }} --nologo

# Clean build outputs and artifacts directory
[unix]
clean-all: clean
    rm -rf artifacts

[windows]
clean-all: clean
    if (Test-Path -LiteralPath artifacts) { Remove-Item -LiteralPath artifacts -Recurse -Force }

# --- Packaging ---

# Pack the global tool NuGet package
pack:
    dotnet pack {{ host-project }} -c Release -o {{ nupkg-dir }}

# Update or install global `roslynmcp` from nuget.org (package id Darylmcd.RoslynMcp)
# Stops one owned Layer 1 process (identified via ROSLYNMCP_REINSTALL_PROCESS_ID +
# ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC, matched by image path under the tool
# store) before mutating, then fails closed naming any holder it cannot attribute.
tool-update:
    pwsh -NoProfile -File ./eng/stop-owned-tool-store-process.ps1
    dotnet tool update -g Darylmcd.RoslynMcp || dotnet tool install -g Darylmcd.RoslynMcp
    dotnet tool list -g

# Pack Release nupkg, then install that build as the global tool (maintainer / dogfood).
# Uninstalls legacy package id RoslynMcp if present, then installs Darylmcd.RoslynMcp from nupkg/.
# Set ROSLYNMCP_REINSTALL_PROCESS_ID and ROSLYNMCP_REINSTALL_PROCESS_STARTED_AT_UTC together
# to identify one owned roslynmcp process that must exit before a Windows reinstall.
tool-install-local: pack
    pwsh -NoProfile -File ./eng/reinstall-local-tool.ps1 -PackageSource "{{ nupkg-dir }}" -ProjectPath "{{ host-project }}"

# Reload the Claude Code plugin from the local repo (Layer 2)
plugin-reload:
    pwsh -NoProfile -File ./eng/update-claude-plugin.ps1

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
    pwsh -NoLogo -NoProfile -File ./eng/verify-nuget-audit.ps1 -SolutionPath {{ solution }}

# --- Repo Hygiene ---

# Run the full release verification pipeline (restore, build, test with coverage, publish, hash manifest)
verify-release:
    pwsh -NoProfile -File ./eng/verify-release.ps1

# Run the pull-request release lane (coverage and live-network tests are informational)
verify-release-pr:
    pwsh -NoProfile -File ./eng/verify-release.ps1 -NoCoverage -ExcludeNetworkTests
