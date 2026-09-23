# Setup, build, and distribution

## Prerequisites

| Requirement | Notes |
|---------------|--------|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Repository build floor: `10.0.400` in `global.json` (rollForward `latestFeature`); CI also verifies that exact floor. |
| Git | For clone and CI artifact workflows. |
| Docker (optional) | Only if you build or run the container image (`Dockerfile`). |

## Packaging / distribution inventory

| Form | Config or source | Command(s) | Notes |
|------|------------------|------------|-------|
| Local build | `RoslynMcp.slnx` | `dotnet build RoslynMcp.slnx --nologo` | Primary solution file. |
| Test | `RoslynMcp.slnx` | `dotnet test RoslynMcp.slnx --nologo` | Test fixtures are prepared automatically; a green run reports all tests passed. |
| Run from source (stdio MCP) | `src/RoslynMcp.Host.Stdio/` | `dotnet run --project src/RoslynMcp.Host.Stdio` | Starts and then waits silently on stdin for MCP frames; point an MCP client at this command and set `ROSLYNMCP_SANCTIONED_ROOTS`. |
| Full release validation | `eng/verify-release.ps1` | `./eng/verify-release.ps1` (or `-Configuration Release`) | Restore, build, test (with Cobertura under `artifacts/coverage/`), publish to `artifacts/publish/host-stdio`, SHA256 manifest under `artifacts/manifests/`. See `docs/coverage-baseline.md`. |
| AI documentation validation | `eng/verify-ai-docs.ps1` | `./eng/verify-ai-docs.ps1` | Same script as CI. |
| NuGet global tool | `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj` (`PackAsTool`, `ToolCommandName`: `roslynmcp`) | `dotnet pack src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj -c Release -o ./nupkg` then `dotnet tool install -g Darylmcd.RoslynMcp --add-source ./nupkg` | Package id `Darylmcd.RoslynMcp` (the unprefixed `RoslynMcp` is owned by another publisher on nuget.org). CLI command name remains `roslynmcp`. |
| Zero-install (`dnx`) | NuGet package `Darylmcd.RoslynMcp` | `dnx Darylmcd.RoslynMcp` (.NET 10 SDK 10.0.100+) | Resolves the package from NuGet on demand; starts and then appears to hang, waiting for MCP frames on stdin. See [Zero-install via `dnx`](#zero-install-via-dnx). |
| Publish + reinstall local tool | MSBuild target `PackAndReinstallGlobalTool` | `dotnet publish -c Release -p:ReinstallTool=true` (Windows; use `-p:` rather than `/p:` under Git Bash) | Uninstalls the prior global install, packs to `nupkg/`, reinstalls. Stops only an explicitly identified, owned `roslynmcp` process (never a name-wide kill); see [reinstall.md](reinstall.md). |
| Docker image | `Dockerfile` | `docker build -t roslynmcp .` then `docker run ...` | Runtime stage uses full SDK (MSBuild workspace). See comments in `Dockerfile` for read-only/volume hints. |
| Sample solutions | `samples/*/` | `dotnet build` per sample `.slnx` | Used for integration tests and manual scenarios. |

## Configure the filesystem boundary

Path-accepting tools and query-anchored solution discovery use a server-owned allowlist. Set
`ROSLYNMCP_SANCTIONED_ROOTS` before starting the host. Separate multiple roots with the platform's
`Path.PathSeparator`: semicolon (`;`) on Windows, colon (`:`) on macOS/Linux. Relative paths are
resolved against the host process working directory, so a repository-scoped `.mcp.json` normally
uses `.`:

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "roslynmcp",
      "env": {
        "ROSLYNMCP_SANCTIONED_ROOTS": "."
      }
    }
  }
}
```

An empty allowlist rejects path access. `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` temporarily
restores the former unbounded behavior only when no roots are configured; it never bypasses a
non-empty configured boundary. Client Roots are not an authority and cannot widen configured
access. Sibling-worktree widening requires both the server-owned
`ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` setting and `expandSanctionedRoots=true` on the individual
`workspace_load` request. Both default to false; request input alone cannot widen access, and the
widening is limited to each configured root's immediate parent. See
[ADR 0002](decisions/0002-configured-sanctioned-root-boundary.md) for the decision and migration
details.

`.` resolves against the server process's working directory, which your MCP client chooses; use an
absolute path if you want the boundary pinned regardless of how the server is launched.

## Zero-install via `dnx`

`dnx` is the .NET SDK's `npx`-equivalent: it resolves a tool package from NuGet on demand, without
installing a global shim. It requires **.NET 10 SDK 10.0.100 or later** (`dnx` ships with the SDK).

One-shot smoke test:

```bash
dnx Darylmcd.RoslynMcp
```

The process should start and then appear to hang. That is expected: it is an MCP server waiting for
protocol messages on stdin. `dnx` is noninteractive unless `--interactive` is requested, so MCP hosts
need no consent flag.

`.mcp.json` snippet:

```json
{
  "mcpServers": {
    "roslyn": {
      "type": "stdio",
      "command": "dnx",
      "args": [
        "Darylmcd.RoslynMcp",
        "--source",
        "https://api.nuget.org/v3/index.json"
      ],
      "env": { "ROSLYNMCP_SANCTIONED_ROOTS": "." }
    }
  }
}
```

Trade-offs versus the global tool:

- No PATH pollution and no manual install step.
- Each cold start resolves to the latest version unless pinned (there is no `dotnet tool update` step).
- The first invocation pays a package-download cost.
- For reproducible setups, pin the version in the package token: `Darylmcd.RoslynMcp@<version>`.

A copy-paste config also lives at [`docs/mcp-json-examples/dnx.mcp.json`](mcp-json-examples/dnx.mcp.json).

## Environment variables

The server starts with built-in operational defaults. File-path access is the exception: configure
`ROSLYNMCP_SANCTIONED_ROOTS` explicitly (usually `.` in a project-scope `.mcp.json`). Multiple roots
use the platform path separator (`;` on Windows, `:` on macOS/Linux). All other `ROSLYNMCP_*` values
are optional literal `env` overrides. Copy-ready examples live in
[mcp-json-examples/README.md](mcp-json-examples/README.md).

| Variable | Default | Purpose |
|----------|---------|---------|
| `ROSLYNMCP_SANCTIONED_ROOTS` | empty (deny path access) | Server-owned path-validation and solution-discovery boundary; `.` resolves against the server process's cwd, which for a plugin-launched server is the session's own working directory (see [Claude Code Plugin](#claude-code-plugin)) |
| `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` | `false` | Temporary compatibility escape hatch for the **empty**-boundary case only: allows path access when no roots are configured. It never bypasses a non-empty boundary. Prefer configuring roots |
| `ROSLYNMCP_ALLOW_ROOT_EXPANSION` | `false` | Allows a request with `expandSanctionedRoots=true` to reach sibling worktrees under each sanctioned root's immediate parent; both opt-ins are required |
| `ROSLYNMCP_MAX_WORKSPACES` | `16` | Concurrent workspace cap |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `300` | Build timeout |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `600` | Test timeout |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `5` | Preview-token TTL |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `120` | Per-request timeout |
| `ROSLYNMCP_TOOL_TIERS` | `stable,experimental` | Registered MCP surface tiers. Set `stable` to expose the closed stable-only workflow (see the README's Live Surface section for its current callable-tool count) to clients that eagerly load discovery definitions; previews whose apply route is experimental are omitted, and experimental requires the stable baseline |
| `ROSLYNMCP_OBSERVABILITY_SINK` | `disabled` | Operator-side diagnostics: `disabled`, structured unexpected failures on `stderr`, or the full enabled `ILogger` stream as bounded JSON lines with `file` |

The packaged NuGet README lists further script, rate-limit, and preview-store variables. For log
destinations, verbosity controls, correlation identifiers, and health probes, see the
[stdio observability contract](stdio-client-integration.md#operator-observability).

## Security model

Loading a solution or project executes MSBuild evaluation. Treat workspaces as trusted code unless
you run the server inside a sandbox, container, or VM.

- Only load repos you trust.
- Use isolation for untrusted workspaces.
- Path validation is defense in depth, not a substitute for trusting the loaded project graph.

**The filesystem boundary is server-owned.** `ROSLYNMCP_SANCTIONED_ROOTS` is configured by you, the
operator, not by the connecting client. A client's MCP Roots can only *narrow* that boundary; they
can never widen it or act as the sole authority. This is deliberate: the control exists to constrain
the **agent**, so a model that is confused or prompt-injected into reading outside your project
cannot do so, even if it asks. Sibling-worktree widening needs two independent opt-ins, the server
operator setting `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true` *and* the request setting
`expandSanctionedRoots=true`, so request input alone never widens access.

Paths are canonicalized component by component, resolving every symlink and junction in the ancestor
chain before comparison, so a file under a linked ancestor cannot present an in-boundary logical
path while pointing outside it. See [SECURITY.md](../SECURITY.md) for the disclosure policy.

## Upgrading from 2.x

Path validation is now bounded by a server-owned root list instead of the client's (deprecated)
`roots/list` capability. Two things to do before upgrading:

1. **Set `ROSLYNMCP_SANCTIONED_ROOTS`.** An unset boundary is fail-closed: every path-taking tool
   rejects its input. `.` is the normal project-scoped value; see
   [Configure the filesystem boundary](#configure-the-filesystem-boundary) for the delimiter and a
   copy-ready snippet. If you need to defer, `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` restores
   the old unbounded behavior as a temporary measure. Since 3.0.0 the server warns at startup and
   reports `server_info.pathBoundary` when the boundary is missing, so the state is visible before
   your first call rather than after it.
2. **Stop relying on `roots/list` for discovery.** Query-anchored solution discovery no longer calls
   it and scans only configured roots. Pass a file-path argument, configure a root containing
   exactly one solution, call `workspace_load` explicitly, or pass a `workspaceId`.

If you install via the Claude Code plugin or the Desktop extension, both ship the default, but
update **both** layers together. A binary-only update leaves a stale config with no boundary set,
which is the fail-closed case above. Rationale and full detail:
[ADR 0002](decisions/0002-configured-sanctioned-root-boundary.md).

## MCP Registry

The server is published to the official [MCP Registry](https://registry.modelcontextprotocol.io/)
under the name **`io.github.darylmcd/roslyn-mcp`**. MCP-Registry-aware clients, and the downstream
catalogs that mirror the registry (the GitHub MCP Registry, the VS Code and Visual Studio MCP
galleries, and aggregators), can discover and install the server by name.

Manifest: [`.claude-plugin/server.json`](../.claude-plugin/server.json) (name
`io.github.darylmcd/roslyn-mcp`, NuGet package `Darylmcd.RoslynMcp`, runtime `dnx`). Every release
tag republishes it automatically via the `publish-nuget` workflow using GitHub OIDC. You can also
install directly, without a registry-aware client, through the global tool or the Claude Code plugin.

## CI artifacts

GitHub Actions `ci` workflow uploads:

| Artifact name | Contents |
|-----------------|----------|
| `host-stdio-publish` | `artifacts/publish/host-stdio` — published host output. |
| `release-manifests` | `artifacts/manifests` — e.g. SHA256 manifest from `verify-release.ps1`. |
| `code-coverage` | `artifacts/coverage` — Cobertura XML from tests; CI also generates `artifacts/coverage/report` (HtmlSummary) via ReportGenerator. |

Download from the workflow run’s **Artifacts** section. Requires a GitHub account with access to the repository.

## Claude Code Plugin

The server is also packaged as a **Claude Code plugin** with bundled skills and safety hooks plus a release-pinned `dnx` launch configuration.

| Form | Config or source | Command(s) | Notes |
|------|------------------|------------|-------|
| Plugin marketplace install | `.claude-plugin/marketplace.json` | `/plugin marketplace add darylmcd/Roslyn-Backed-MCP` then `/plugin install roslyn-mcp@roslyn-mcp-marketplace` | Installs 32 skills, hooks, and an exact-version `dnx` launcher. Requires .NET 10 SDK 10.0.100+ and NuGet access on first launch; no global `roslynmcp` shim is required. |
| Plugin local dev | `.claude-plugin/plugin.json`, `skills/`, `hooks/` | `claude --plugin-dir /path/to/Roslyn-Backed-MCP` | Load plugin from local checkout for testing. |
| Plugin validation | `.claude-plugin/` | `claude plugin validate .` | Validates plugin and marketplace manifests. |

Plugin components:

| Directory | Contents |
|-----------|----------|
| `.claude-plugin/` | Plugin manifest (`plugin.json`) and marketplace descriptor (`marketplace.json`) |
| `skills/` | 32 SKILL.md files. Analysis/review: analyze, review, complexity, security, test-coverage, dead-code, explain-error, architecture-review, impact-assessment, trace-flow, di-audit, inheritance-explorer, exception-audit. Refactor: refactor, refactor-loop, extract-method, code-actions, migrate-package, modernize. Search: semantic-find. Tests: test-triage, generate-tests. Session / workspace: session-undo, snippet-eval, project-inspection, workspace-health, format-sweep. Documentation: document. Release: update, version-bump, nuget-preflight, mcp-server-surface-test. |
| `hooks/` | `hooks.json` with PostToolUse prompt hooks only: a compile-check reminder after structural `*_apply` calls and a server-update notice after `server_info`. Preview-before-apply is enforced server-side by required preview tokens, not by a hook. |

The skill families above are the full skill list (one `SKILL.md` per skill under `skills/`).

### Which repo does a plugin-launched server analyze?

`.claude-plugin/mcp.json` ships `ROSLYNMCP_SANCTIONED_ROOTS: "."` so a plugin-launched server works
out of the box. But `.` still means "the server process's cwd," and for a plugin-launched server that
cwd is wherever your Claude Code session started, not necessarily the repo you want to analyze. A
session started outside the target repo (for example `~/.claude` or your home directory) has `.`
resolve there instead: every path-taking tool then fails closed, and `server_info.pathBoundary`
reports the boundary that does not cover your repo. Three supported ways to point a plugin-launched
session at a different repo, each with its own trade-off:

1. **Start the session inside the target repo.** `.` then resolves correctly with no config change.
   This is the common case, and the only one with no fail-closed edge to reason about.
2. **Override `ROSLYNMCP_SANCTIONED_ROOTS` via the host's own config layering.** Add a project-scope
   `.mcp.json` in the target repo with an absolute-path `env.ROSLYNMCP_SANCTIONED_ROOTS` for the
   server; the boundary then stays fail-closed and pinned regardless of session cwd. Plugin servers
   are namespaced by Claude Code, so confirm in your client that the entry replaces the plugin's
   launch rather than registering a second server.
3. **Reach a sibling worktree without changing roots.** Set `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true`
   (server-owned) and pass `expandSanctionedRoots=true` on the individual request; both opt-ins are
   required and the boundary stays fail-closed otherwise (see [Security model](#security-model)).

## Related

- Agent-facing commands and MCP policy: `ai_docs/runtime.md`
- Product and release expectations: `docs/product-contract.md`, `docs/release-policy.md`
