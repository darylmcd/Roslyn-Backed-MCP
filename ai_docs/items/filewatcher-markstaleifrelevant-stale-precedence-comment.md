# filewatcher-markstaleifrelevant-stale-precedence-comment — comment claims external-edit precedence the code doesn't implement

**row:** `filewatcher-markstaleifrelevant-stale-precedence-comment` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:151-156` — the `MarkStaleIfRelevant` inline comment ("External edits take precedence: once set, a subsequent explicit MarkStale(\"apply\") does not downgrade.")

## Acceptance

- [ ] The stale claim is fixed or deleted so no comment asserts external-edit precedence / no-downgrade. The actual behavior is unconditional **last-writer-wins** — `MarkStaleWithReason` (`FileWatcherService.cs:247`) does `_staleReason = reason` with no precedence guard, and its own `<summary>` + the class `<remarks>` both state last-writer-wins.

## Evidence

- Cold-review of the 2026-06-21 top-n-remediation run flagged the contradiction: `MarkStaleIfRelevant` (lines 151-156) claims "External edits take precedence: once set, a subsequent explicit MarkStale(\"apply\") does not downgrade", but `MarkStaleWithReason` is unconditional last-writer-wins (`:243-253`, `_staleReason = reason`). Predates this run (`git blame` → #267, April 2026); now also contradicts the corrected class `<remarks>` and the `filewatcher-class-xmldoc-truncated` clause completed in the same run.

## Context

Pure doc-accuracy fix, 1 file, 1 comment. The inline comment was authored before the last-writer-wins semantics were settled and never updated. Low priority; the runtime behavior is correct — only the comment lies.
