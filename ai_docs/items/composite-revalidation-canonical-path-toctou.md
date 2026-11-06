# composite-revalidation-canonical-path-toctou — Canonical path from root validation is discarded before the write

**row:** `composite-revalidation-canonical-path-toctou` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolDispatch.cs`

## Acceptance

- [ ] The validated canonical path is what is written, or the write is refused on a changed target; a link-swap regression test is red first.

## Evidence

- PR 1997 cold review follow-up 5: ValidatePathAgainstRootsAsync result discarded (pre-existing on the IPreviewStore route).
