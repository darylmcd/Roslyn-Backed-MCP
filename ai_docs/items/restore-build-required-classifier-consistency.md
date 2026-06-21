# restore-build-required-classifier-consistency — unify build-required diagnostic classification

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs` (`HasBuildRequiredWorkspaceDiagnostics` — matches ANY `WORKSPACE_UNRESOLVED_ANALYZER`)
- `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs` (`HasBuildRequiredDiagnostic` — requires message substrings; `ResolveReadinessVerdict`/`BuildReadinessSignals` re-derive from raw diagnostics, ignoring the new flag)
- `src/RoslynMcp.Core/Models/WorkspaceStatusDto.cs` (`BuildRequired` flag)

## Acceptance

- [ ] One shared build-required diagnostic classifier; both the `BuildRequired` flag computation and the `workspace_readiness_report` verdict call it.
- [ ] Regression test: a `WORKSPACE_UNRESOLVED_ANALYZER` whose message lacks the magic substrings yields the SAME build/restore verdict from both the summary DTO and the readiness report (they cannot diverge).

## Evidence

Two divergent build-required definitions coexist after PR #1009 (restore-required/buildRequired split): `WorkspaceManager.HasBuildRequiredWorkspaceDiagnostics` matches any analyzer warning while `WorkspaceTools.HasBuildRequiredDiagnostic` requires message substrings; the readiness verdict bypasses the new flag entirely. Benign today (no correctness regression — shipped behavior is individually reasonable), but the two surfaces can disagree for an analyzer warning lacking the substrings. Source: 2026-06-21 backlog-sweep code-quality review of PR #1009 (reviewer severity: medium).

## Context

Consistency/debt follow-on to the build-vs-restore split. Not urgent — each surface is independently correct; this unifies them so they cannot drift.
