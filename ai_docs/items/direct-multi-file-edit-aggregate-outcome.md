# direct-multi-file-edit-aggregate-outcome — Report the actual outcome of direct edit batches

**row:** `direct-multi-file-edit-aggregate-outcome` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Verify actual disk writes, the experimental apply_multi_file_edit success/count result and ledger for mixed and entirely refused batches. Count only successful writes and preserve truthful per-file results and recovery for earlier writes.

## Evidence

- Cold review of repeated-target preflight found that direct apply returns true with results.Count even when syntax validation records zero edits or persistence returns a failure. The change ledger also includes every requested file. Source-derived.

## Notes

5.x screen at d9e2b3ff. The direct batch route is experimental, but ApplyTextEditsCoreAsync calls TryApplyChanges before its disk fallback. A false result after that point can coexist with a native write or an in-memory change. It also serves stable apply_text_edit. Filtering summaries alone leaves incorrect recovery and ledger effects. Select after confirming a complete compensation and receipt fix preserves the stable apply_text_edit and workspace_changes contracts, or at the next approved major.

Remediation run 2026-11-21 (planned, not implemented; cold review): NOT size S. Slice A (move syntax preflight ahead of undo capture; capture/count/record only files actually written; all-refused batch returns success=false, filesModified=0, no capture/ledger/verify; apply_multi_file_edit is experimental, EditService.cs:187/:205/:279-282) is not correct alone because a persist failure after TryApplyChanges (EditService.cs:702, :709-713) leaves the workspace (maybe disk) changed while the file is reported unwritten and dropped from ledger/FilesModified. The row requires Slice B: compensation on persist failure (shared core also used by stable apply_text_edit: Rule 1 evidence; needs a fault-injection seam) plus a decision for mid-batch exception/cancellation (:256-257 leaves earlier writes with a pending capture and no ledger entry). Also: CompositeApplyOrchestrator.cs:82-83 aggregate not probed (verify); correct the stale atomic wording in MultiFileEditTools.cs:24-36. Overlaps: undo-capture-call-site-cleanup, edit-path-guard-ancestor-swap (guard line moves with the capture loop).
