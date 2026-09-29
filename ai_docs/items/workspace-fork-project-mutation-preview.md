# workspace-fork-project-mutation-preview — Align fork apply with its claim

**row:** `workspace-fork-project-mutation-preview` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceForkApplyService.cs:81-170`
- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs:69-77`
- `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`

## Acceptance

- [ ] `workspace_fork_apply` either safely replays a project-mutation preview in the fork or narrows its public description so it does not claim to accept any `*_preview` token.
- [ ] A red-first test checks a project-mutation token and verifies the source workspace is unchanged; if unsupported, a contract test checks the corrected description and safe refusal.

## Evidence

- Parent `preview-token-store-mismatch-false-stale`: `WorkspaceForkApplyService` reads only `IPreviewStore` while the tool advertises a broader token set.

## Context

- Distinct fork capability/contract mechanism from ordinary apply routing and token consumption errors.
