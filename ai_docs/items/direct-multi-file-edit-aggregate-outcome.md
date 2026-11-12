# direct-multi-file-edit-aggregate-outcome — Report the actual outcome of direct edit batches

**row:** `direct-multi-file-edit-aggregate-outcome` · **pri:** `Medium` · **size:** `S` · **verified:** `a12412bbe4bd31a18cbdd8f481475587e1697c97`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Verify actual disk writes, the experimental apply_multi_file_edit success/count result and ledger for mixed and entirely refused batches. Count only successful writes and preserve truthful per-file results and recovery for earlier writes.

## Evidence

- Cold review of repeated-target preflight found that direct apply returns true with results.Count even when syntax validation records zero edits or persistence returns a failure. The change ledger also includes every requested file. Source-derived.
