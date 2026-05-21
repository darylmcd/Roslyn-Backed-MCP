# Workspace fork apply primitive - design note

<!-- purpose: Decide the bounded implementation shape for an isolated workspace fork/apply/validate primitive. -->
<!-- scope: in-repo -->

**Initiative:** `workspace-fork-apply-primitive`
**Date:** 2026-05-21
**Scope:** design-only; no runtime tool implementation in this row.
**Outcome:** go for a new experimental validation-bundle tool, split into `workspace-fork-apply-tool`.

## Existing-tool Delta

Today an agent that wants to test writes without touching the loaded checkout has to create a sibling clone or `.worktrees/<id>/`, call `workspace_load` on that path, run apply tools, validate, and manually delete the fork. That sequence is outside the server's ownership boundary, so path-sanctioning, cleanup, and failure recovery vary by prompt.

`apply_with_verify` is close but not equivalent: it applies a preview into the current workspace, verifies, and can revert. It still mutates the live tree during the attempt. The missing primitive is "fork first, apply and verify inside the fork, then either discard or keep the fork".

## Tool Shape

Add an experimental tool in the validation-bundle family:

`workspace_fork_apply(workspaceId, previewToken, retention = "drop-on-success", runTests = false, testFilter = null, forkName = null)`

Input:

| Field | Type | Required | Notes |
|---|---|---|---|
| `workspaceId` | string | yes | Source workspace session. |
| `previewToken` | string | yes | Existing preview token produced against the source workspace. |
| `retention` | enum | no | `drop-on-success`, `drop-on-failure`, `drop-always`, `keep`. Default `drop-on-success`. |
| `runTests` | bool | no | Runs discovered/explicit tests after compile. |
| `testFilter` | string | no | Optional explicit filter when `runTests=true`. |
| `forkName` | string | no | Optional stable suffix for diagnostics. Server sanitizes; callers do not choose arbitrary paths. |

Output:

| Field | Type | Notes |
|---|---|---|
| `success` | bool | True only when apply and validation pass. |
| `forkWorkspaceId` | string? | Present when retained or when failure diagnostics need a workspace id. |
| `forkPath` | string | Absolute path under server-owned temp/fork root. |
| `retained` | bool | Whether the fork remains on disk after the call. |
| `appliedFiles` | string[] | Files changed inside the fork. |
| `validation` | object | Same verdict vocabulary as `validate_workspace`. |
| `cleanupWarnings` | string[] | Non-fatal cleanup failures. |

## Disk Lifecycle

The server owns fork location. It should create forks under a server-managed directory inside the source repo's sanctioned boundary, for example `<repo>/.roslynmcp/forks/<timestamp>-<slug>/`, and should ignore that subtree in file watchers just like `.worktrees/`.

Recommended implementation: create a disposable git worktree when the source root is a git worktree; otherwise fall back to a recursive directory copy. Worktree is preferred because it preserves project graph shape and avoids copying package/build output when the repo is clean. Directory copy is a fallback for non-git fixtures.

The tool must not accept arbitrary destination paths. This keeps path validation simple: the source workspace is already sanctioned, and the fork lives below a server-owned child directory of the same sanctioned root.

## Retention Semantics

| Retention | On success | On failure | Use case |
|---|---|---|---|
| `drop-on-success` | delete fork | keep fork | Default CI-like probe: preserve failures only. |
| `drop-on-failure` | keep fork | delete fork | Caller wants successful candidate for manual inspection. |
| `drop-always` | delete fork | delete fork | Strict no-artifact mode. |
| `keep` | keep fork | keep fork | Long manual follow-up/debugging. |

Cleanup always closes the fork workspace first and drains build servers before deleting the directory.

## Failure Modes

- Fork creation fails: return `success=false`, no apply attempted, no preview invalidation.
- Preview token is stale before fork: return the existing preview-stale category and do not create a fork.
- Apply succeeds but compile/test fails: return validation diagnostics and retain/drop by retention policy.
- Cleanup fails: return `success=false` only when requested retention requires deletion and deletion could not be completed. Include `cleanupWarnings` either way.
- Source workspace reloads during the operation: refuse with a retry hint; do not apply a preview against a mismatched source snapshot.

## Restart Behavior

Fork metadata should be reconstructable from disk. On server restart, retained forks are not automatically loaded. A future `workspace_load` on the retained fork path can recover it. Non-retained abandoned forks under the server-owned fork root are eligible for best-effort cleanup on the next `workspace_load` or an explicit cleanup tool.

## Follow-on Row

`workspace-fork-apply-tool` | High | none | Implement experimental `workspace_fork_apply` as a validation-bundle tool that forks the loaded workspace into a server-owned child directory, applies an existing preview token inside the fork, runs compile/test validation, and deletes or retains the fork according to `retention`. Anchors: `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`, `src/RoslynMcp.Host.Stdio/Tools/WorkspaceTools.cs`, `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`, `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs`, `tests/RoslynMcp.Tests/ValidationBundleToolsTests.cs`, `tests/RoslynMcp.Tests/Workspace/WorkspaceForkApplyTests.cs`. Regression test shape: fork is created under the source root, preview apply mutates only the fork, compile validation returns a verdict, retained failure forks can be loaded, and cleanup closes/drains before deletion. Evidence: `ai_docs/items/workspace-fork-apply-primitive.md`.
