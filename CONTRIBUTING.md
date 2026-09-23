# Contributing to Roslyn-Backed MCP Server

Thank you for your interest in contributing. This guide covers the essentials.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — `10.0.400` per [`global.json`](global.json) (`rollForward` is `latestFeature`; any compatible later 10.0.x feature band works)
- Git

## Build and Test

```bash
dotnet build RoslynMcp.slnx
dotnet test RoslynMcp.slnx
```

For a full release verification (restore, build, test, publish, hash manifests):

```powershell
./eng/verify-release.ps1
```

## Branch Workflow

See `ai_docs/workflow.md` for the canonical branch, worktree, and pull-request policy.

- Create a feature branch from `main`.
- Keep commits focused and well-described.
- Ensure CI passes before requesting review.

## Pull Request Expectations

- All existing tests must pass.
- New features and bug fixes should include test coverage.
- Run the local CI mirror before opening the PR: `just ci` (requires [`just`](https://github.com/casey/just)), which runs the doc, skill, format, workflow-lint, release-PR, and vulnerability-audit gates. `./eng/verify-release.ps1` runs the full build/test/publish verification.
- Add a changelog fragment at `changelog.d/<row-id>.md` for any shipped, test, build, workflow, skill, or public-documentation change (CI enforces this). Use the YAML-frontmatter format described in [`changelog.d/README.md`](changelog.d/README.md); do not edit `CHANGELOG.md` directly.
- Follow the existing code style and patterns.

## Coding Conventions

- **Nullable reference types** are enabled (`<Nullable>enable</Nullable>`).
- **Warnings are errors** (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`).
- Use `ConfigureAwait(false)` on all internal async calls.
- Follow the existing tool/service layering:
  - `RoslynMcp.Core` — DTOs and service interfaces only.
  - `RoslynMcp.Roslyn` — Roslyn-backed implementations.
  - `RoslynMcp.Host.Stdio` — MCP tool/resource/prompt wrappers.

## CI Validation

[`CI_POLICY.md`](CI_POLICY.md) is the canonical contract for what CI runs and what gates a merge. It varies by change type: documentation-only pull requests take a lighter route, while code-bearing pull requests run the sharded Windows and Linux release build/test matrix plus an exact-SDK-floor probe. Gates include AI-doc validation, the changelog-fragment contract, workflow linting, the changed-file format check, and the vulnerable-package audit. `just ci` mirrors the local subset.

## Filing Surface-Test Findings

If you find a bug or behaviour gap while running [`/mcp-server-surface-test`](skills/mcp-server-surface-test/README.md) against your own C# repo, share it back via the [Surface-test finding](https://github.com/darylmcd/Roslyn-Backed-MCP/issues/new?template=mcp-server-surface-test-finding.yml) issue template. The shipped skill renders findings into a copy-paste body block by default; pass `--auto-file` and the skill calls `gh issue create` for you.

P0 / `area: security` findings are refused for public filing; see [SECURITY.md](SECURITY.md) for the private-disclosure path.

## Building And Running From Source

```bash
dotnet build RoslynMcp.slnx --nologo
dotnet test RoslynMcp.slnx --nologo
dotnet run --project src/RoslynMcp.Host.Stdio
```

`dotnet run` starts the stdio host, which then waits silently on stdin for MCP frames. To use a source build from an MCP client, point the client at that command and set `ROSLYNMCP_SANCTIONED_ROOTS` (see [`docs/setup.md`](docs/setup.md#configure-the-filesystem-boundary)).

## Reporting Issues

Open an issue on GitHub. Include:

- Steps to reproduce
- Expected vs actual behavior
- .NET SDK version and OS
