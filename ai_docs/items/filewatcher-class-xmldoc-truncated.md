# filewatcher-class-xmldoc-truncated — FileWatcherService class XML doc clause is truncated

**row:** `filewatcher-class-xmldoc-truncated` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs` — the class-level `<para>` block (~lines 22-27), ending: "…server apply paths that want to preserve their attribution mark after the on-disk commit settles."

## Acceptance

- [ ] The class XML-doc sentence is completed (supply the missing main clause) or trimmed so it reads as a grammatical statement; no dropped-clause fragment remains.

## Evidence

- Row-1 implementer finding (2026-06-20 top-n-remediation, `filewatcher-waitforstale-clearstale-stranded-awaiter`): the clause "…server apply paths that want to preserve their attribution mark after the on-disk commit settles." has no main verb — it sets up a subject ("server apply paths that want to preserve…") and trails off, reading as if a clause was dropped during an edit.

## Context

Cosmetic doc-comment fix, 1 file. Worth doing when next editing `FileWatcherService` to keep the (otherwise detailed and load-bearing) class documentation accurate.
