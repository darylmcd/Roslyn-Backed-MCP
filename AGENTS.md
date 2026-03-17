# AGENTS Guide

This file is the fast-start operating guide for AI coding agents working in this repository.

## 30-Second Startup Checklist

1. Read `README.md` and this file.
2. Load workspace and capture `workspaceId`.
3. Read `server_info` and `roslyn://server/catalog`.
4. Prefer stable tools/resources first.
5. Use preview/apply for mutations, then validate with build/tests.

## One-Screen Decision Matrix

| If you need to... | Start here | Primary location |
|---|---|---|
| understand support tiers and boundaries | `docs/product-contract.md` | `docs/` |
| confirm release and compatibility gates | `docs/release-policy.md` | `docs/`, `eng/verify-release.ps1` |
| navigate or analyze C# code semantically | stable symbol/diagnostic tools | `src/Company.RoslynMcp.Roslyn/` |
| change host wrapper/catalog/prompt wiring | MCP tool/resource/prompt wrappers | `src/Company.RoslynMcp.Host.Stdio/` |
| change DTOs or cross-layer contracts | boundary models/interfaces | `src/Company.RoslynMcp.Core/` |
| perform multi-file/project mutation safely | preview-first experimental tools | `src/Company.RoslynMcp.Roslyn/`, `tests/Company.RoslynMcp.Tests/` |
| validate behavior/regressions | integration and hardening tests | `tests/Company.RoslynMcp.Tests/` |

## 1) Start Here In Every Session

1. Read `README.md` for product shape and support tiers.
2. Read `docs/product-contract.md` for stable vs experimental guarantees.
3. Read `docs/release-policy.md` before making release-impacting changes.
4. Read `docs/parity-gap-matrix.md` and `docs/roadmap.md` for non-goals and deferred scope.
5. Use the command and file maps below to choose the minimal safe path.

## 2) Repository Map

- `src/Company.RoslynMcp.Host.Stdio/`: MCP host process, tool wrappers, catalog/resource/prompt wiring, startup and logging.
- `src/Company.RoslynMcp.Core/`: contract DTOs, shared abstractions, preview-store contracts, cross-layer models.
- `src/Company.RoslynMcp.Roslyn/`: Roslyn-backed workspace, diagnostics, symbols, refactorings, analysis, and execution services.
- `tests/Company.RoslynMcp.Tests/`: integration and behavior tests for stable and experimental surfaces.
- `samples/`: sample solutions used by integration tests and behavior validation.
- `eng/verify-release.ps1`: release verification and publish/hash checks.
- `publish/`: publish output and BuildHost runtime assets.
- `docs/`: contract, policy, parity, and roadmap decisions.

## 3) End-To-End Project Flow

1. Load workspace session:
   - Call workspace-load tool and capture `workspaceId`.
2. Explore and assess:
   - Prefer stable semantic/navigation/diagnostic tools first.
3. Plan edits:
   - Use preview-first refactoring and bounded mutation flows.
4. Apply changes:
   - Apply preview token only if workspace version is unchanged.
5. Validate:
   - Run build/test tools through MCP or `dotnet test RoslynMcp.slnx`.
6. Re-check diagnostics:
   - Confirm no new compiler/analyzer regressions.
7. Finalize:
   - Update docs/tests for any surface or behavior change.

## 4) Stable-First Decision Rule

- Default to stable tools/resources whenever they satisfy the task.
- Use experimental tools only when they materially reduce risk or effort.
- If experimental behavior is used, document it in the PR/release notes.

## 5) Build, Test, Release Commands

- Build: `dotnet build RoslynMcp.slnx --nologo`
- Test: `dotnet test RoslynMcp.slnx --nologo`
- Run host: `dotnet run --project src/Company.RoslynMcp.Host.Stdio`
- Verify release: `./eng/verify-release.ps1`

## 6) Where To Change What

- Add or update MCP tool wrappers/catalog entries:
  - `src/Company.RoslynMcp.Host.Stdio/`
- Change DTO contract or cross-layer model behavior:
  - `src/Company.RoslynMcp.Core/`
- Implement Roslyn semantic/refactoring logic:
  - `src/Company.RoslynMcp.Roslyn/`
- Validate behavior and guard regressions:
  - `tests/Company.RoslynMcp.Tests/`

## 7) Common Pitfalls

- Do not assume unsaved editor buffer parity; this host uses on-disk state via `MSBuildWorkspace`.
- Do not bypass preview/apply for destructive refactoring flows.
- Do not treat prompts as compatibility-stable API surface.
- Do not broaden stable contracts without updating product contract and release policy docs.

## 8) Definition Of Done For Agent Changes

- Code builds and relevant tests pass.
- Surface changes are reflected in catalog and docs.
- Stable/experimental tiering remains consistent.
- Non-goals and boundaries remain explicit.
