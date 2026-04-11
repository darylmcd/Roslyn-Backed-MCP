# Operational Essentials

Compact reminder layer aligned with `ai_docs/workflow.md`.

## Build / Test / Lint Commands

All commands are available as `just` recipes (`just --list` for the full menu).

| Action | Recipe | Raw command |
|--------|--------|-------------|
| Build | `just build` | `dotnet build RoslynMcp.slnx --nologo` |
| Test | `just test` | `dotnet test RoslynMcp.slnx --nologo` |
| Full validation | `just ci` | `./eng/verify-release.ps1` + docs + vuln audit |
| AI-doc validation | `just verify-docs` | `./eng/verify-ai-docs.ps1` |
| Run host | `just run` | `dotnet run --project src/RoslynMcp.Host.Stdio` |

## Branching And Isolation

- Use a task branch before production-code edits.
- Use a dedicated worktree when concurrent write-capable sessions are active or likely.

## Commit Format

- Imperative subject line, ≤72 chars
- Reference backlog item IDs in body when applicable (e.g., `BUG-08`, `FEAT-01`)

## Roslyn MCP (C#)

- Connect the **`roslyn`** MCP server (repo `.mcp.json`: `roslynmcp` stdio).
- For C# edits, use Roslyn MCP **refactoring** tools (`rename_*`, `extract_*`, `code_fix_*`, etc.) when available—not only navigation/diagnostics.
- Use preview → apply; pass `workspaceId` and respect workspace version for mutations.

## Merge-Ready Handoff

- Follow `CI_POLICY.md` before merge handoff.
- Run `./eng/verify-release.ps1` for code changes; `./eng/verify-ai-docs.ps1` for doc-only changes.
- Sync with base branch if repository settings require it.

## Ownership

- Canonical git/worktree/PR policy: `ai_docs/workflow.md`
- Canonical validation and merge gating: `CI_POLICY.md`
- Canonical runtime context and Roslyn MCP agent policy: `ai_docs/runtime.md`
- Implementation quality and safety rules: `.github/copilot-instructions.md`
