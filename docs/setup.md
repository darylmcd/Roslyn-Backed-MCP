# Setup, build, and distribution

## Prerequisites

| Requirement | Notes |
|---------------|--------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Pin: `10.0.100` in `global.json` (rollForward `latestFeature`); any 10.0.x patch generally works. |
| Git | For clone and CI artifact workflows. |
| Docker (optional) | Only if you build or run the container image (`Dockerfile`). |

## Packaging / distribution inventory

| Form | Config or source | Command(s) | Notes |
|------|------------------|------------|-------|
| Local build | `RoslynMcp.slnx` | `dotnet build RoslynMcp.slnx --nologo` | Primary solution file. |
| Test | `RoslynMcp.slnx` | `dotnet test RoslynMcp.slnx --nologo` | |
| Run from source (stdio MCP) | `src/RoslynMcp.Host.Stdio/` | `dotnet run --project src/RoslynMcp.Host.Stdio` | |
| Full release validation | `eng/verify-release.ps1` | `./eng/verify-release.ps1` (or `-Configuration Release`) | Restore, build, test (with Cobertura under `artifacts/coverage/`), publish to `artifacts/publish/host-stdio`, SHA256 manifest under `artifacts/manifests/`. See `docs/coverage-baseline.md`. |
| AI documentation validation | `eng/verify-ai-docs.ps1` | `./eng/verify-ai-docs.ps1` | Same script as CI. |
| NuGet global tool | `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` (`PackAsTool`, `ToolCommandName`: `roslynmcp`) | `dotnet pack src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj -c Release -o ./nupkg` then `dotnet tool install -g Darylmcd.RoslynMcp --add-source ./nupkg` | Package id `Darylmcd.RoslynMcp` (the unprefixed `RoslynMcp` is owned by another publisher on nuget.org). CLI command name remains `roslynmcp`. |
| Publish + reinstall local tool | MSBuild target `PackAndReinstallGlobalTool` | `dotnet publish -c Release /p:ReinstallTool=true` (Windows) | Uninstalls prior global install, packs to `nupkg/`, reinstalls. Uses `taskkill` on `roslynmcp.exe`. |
| Docker image | `Dockerfile` | `docker build -t roslynmcp .` then `docker run ...` | Runtime stage uses full SDK (MSBuild workspace). See comments in `Dockerfile` for read-only/volume hints. |
| Legacy Visual Studio solution | `Roslyn-Backed-MCP.sln` | `dotnet build Roslyn-Backed-MCP.sln` | Prefer `RoslynMcp.slnx` for day-to-day work. |
| Sample solutions | `samples/*/` | `dotnet build` per sample `.slnx` | Used for integration tests and manual scenarios. |

## CI artifacts

GitHub Actions `ci` workflow uploads:

| Artifact name | Contents |
|-----------------|----------|
| `host-stdio-publish` | `artifacts/publish/host-stdio` — published host output. |
| `release-manifests` | `artifacts/manifests` — e.g. SHA256 manifest from `verify-release.ps1`. |
| `code-coverage` | `artifacts/coverage` — Cobertura XML from tests; CI also generates `artifacts/coverage/report` (HtmlSummary) via ReportGenerator. |

Download from the workflow run’s **Artifacts** section. Requires a GitHub account with access to the repository.

## Claude Code Plugin

The server is also packaged as a **Claude Code plugin** with 10 curated skills and safety hooks.

| Form | Config or source | Command(s) | Notes |
|------|------------------|------------|-------|
| Plugin marketplace install | `.claude-plugin/marketplace.json` | `/plugin marketplace add darylmcd/Roslyn-Backed-MCP` then `/plugin install roslyn-mcp@roslyn-mcp-marketplace` | Installs MCP server + 11 skills + hooks. Requires `roslynmcp` on PATH. |
| Plugin local dev | `.claude-plugin/plugin.json`, `skills/`, `hooks/` | `claude --plugin-dir /path/to/Roslyn-Backed-MCP` | Load plugin from local checkout for testing. |
| Plugin validation | `.claude-plugin/` | `claude plugin validate .` | Validates plugin and marketplace manifests. |

Plugin components:

| Directory | Contents |
|-----------|----------|
| `.claude-plugin/` | Plugin manifest (`plugin.json`) and marketplace descriptor (`marketplace.json`) |
| `skills/` | 10 SKILL.md files: analyze, refactor, review, document, security, dead-code, test-coverage, migrate-package, explain-error, complexity |
| `hooks/` | `hooks.json` with PreToolUse (preview-before-apply guard) and PostToolUse (compile-check reminder) |

See `README.md` § *Claude Code Plugin Installation* for the full skill table.

## Related

- Agent-facing commands and MCP policy: `ai_docs/runtime.md`
- Product and release expectations: `docs/product-contract.md`, `docs/release-policy.md`
