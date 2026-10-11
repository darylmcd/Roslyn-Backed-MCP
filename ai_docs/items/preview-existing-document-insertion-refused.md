# preview-existing-document-insertion-refused — Restore valid insertion preview

**row:** `preview-existing-document-insertion-refused` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/MultiFileEditTools.cs`
- `tests/RoslynMcp.Tests/PreviewMultiFileEditSyntaxRegressionTests.cs`

## Acceptance

- [ ] Reproduce installed 4.3.0 preview refusal for a ready Windows worktree workspace and existing document; identify the actual exception before choosing the fix.
- [ ] Valid one-character-range insertion, including a comment at line1/column1 and a class property at a verified member boundary, returns a complete preview diff/token and applies through the documented route.
- [ ] Preserve syntax, coordinate, workspace-version, token provenance and stale-preview refusals. Report safe actionable refusal categories without arbitrary exception text.
- [ ] Observe the reproduced regression fail before the fix and pass afterward; cover single-file and multi-file batches without mutating other files on refusal.

## Evidence

- BioRemote executor session2026-10-11: live Roslyn4.3.0; workspace ready after restore/reload, document_symbols resolves the target. No edit applied.
- `preview_multi_file_edit` with existing `D:/BioRemote/.worktrees/refactor-hosted-process-lifecycle-duplication/BioRemoteTests/Connection/Protocol/ProtocolAnydeskTests.cs`, edit start/end858:1, newText `        public Exception? DetachFailure { get; init; }` plus newline returned isError InvalidOperationException: `The operation is not valid in the current state. Check the tool contract and retry.`
- Same refusal for comment-only insertion at start/end1:1 with `// Preview probe.` plus newline; backslash path and CRLF variants also refused. The member boundary was verified semantically; comment-only probe removes member grammar ambiguity.
- Single-file fallback investigation remains pending; this evidence establishes a reproducible refusal, not a diagnosed root cause. Existing preview-store truncation and whitespace-diff rows concern different mechanisms.

## Context

- Filing only; no Roslyn implementation takeover. Preserve public contract-care and release policy when selecting any error-category change.
