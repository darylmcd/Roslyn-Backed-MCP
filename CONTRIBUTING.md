# Contributing to Roslyn-Backed MCP Server

Thank you for your interest in contributing. This guide covers the essentials.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) — `10.0.100` per [`global.json`](global.json) (`rollForward` is `latestFeature`; any compatible 10.0.x patch works)
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
- The CI pipeline (`./eng/verify-release.ps1`) must succeed.
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

See `CI_POLICY.md` for merge-gating expectations. The CI workflow runs:

1. AI-doc validation (`./eng/verify-ai-docs.ps1`)
2. Release build verification (`./eng/verify-release.ps1 -Configuration Release`)
3. Vulnerable package audit

## Reporting Issues

Open an issue on GitHub. Include:

- Steps to reproduce
- Expected vs actual behavior
- .NET SDK version and OS
