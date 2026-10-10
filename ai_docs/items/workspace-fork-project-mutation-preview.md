# workspace-fork-project-mutation-preview — Align fork apply with its claim

**row:** `workspace-fork-project-mutation-preview` · **pri:** `Medium` · **size:** `M` · **deps:** `preview-store-explicit-internal-state`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:81-170`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:69-77`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`

## Acceptance

- [ ] `workspace_fork_apply` replays document, composite and project-mutation previews from owned non-consuming snapshots in the isolated fork; preserve the advertised token-family contract.
- [ ] Replay preserves source workspace bytes, version, undo state and source-token availability, including failure and capture/claim races. Unsupported edits fail before effects with an actionable typed refusal.
- [ ] Red-first tests exercise actual project and composite fork replay, every supported change kind and preview safety metadata; verify generated files, encoding and source isolation. No default safety metadata or destructive source-token retrieval.
- [ ] Re-vet the complete scope after atomic preview safety lands; run the required full producer and cold review before landing.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: `WorkspaceForkApplyService` reads only `IPreviewStore` while the tool advertises a broader token set.

## Context

- Distinct fork capability/contract mechanism from ordinary apply routing and token consumption errors.
