# extract-and-wire-same-file-composition — Compose DI edits on the extracted snapshot

**row:** `extract-and-wire-same-file-composition` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:85`
- `src/RoslynMcp.Roslyn/Services/ExtractAndWireOrchestrator.cs:100`

## Acceptance

- [ ] Rewrite each registration against the current intermediate extraction snapshot; final mutation and review diff retain both extraction and DI edits in a shared physical file.
- [ ] Preserve linked-file agreement, source bytes/version/token and preview-only behavior; cover same-file and different-file controls.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. The loop rereads original disk bytes and UpsertMutation replaces the extraction mutation for a matching path. Retained branch composes onto modifiedSolution before rendering.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
