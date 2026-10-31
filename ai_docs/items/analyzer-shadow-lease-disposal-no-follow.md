# analyzer-shadow-lease-disposal-no-follow — Bound lease disposal deletion to owned shadow root

**row:** `analyzer-shadow-lease-disposal-no-follow` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:625`
- `src/RoslynMcp.Roslyn/Helpers/AnalyzerReferenceIsolation.cs:639`

## Acceptance

- [ ] Lease disposal deletes only the owned shadow root without following a replacement reparse point at the root or any descendant.
- [ ] A replacement junction or supported symlink under the shadow root cannot cause deletion or mutation of an external target; retain bounded cleanup for ordinary owned roots.
- [ ] Cover the disposal path with red-first link-swap regressions on supported platforms, and distinguish Windows privilege limitations in results.

## Evidence

- Read at merged main `261cddb38338c02b0e83db569673c739c12ffca7`: `AnalyzerShadowLease.TryDeleteShadowRoot` uses `Directory.Delete(ShadowRoot, recursive: true)` during lease disposal after a mutable on-disk shadow root. The abandoned-root sweep was separately bounded by PR #1923; this disposal path remains independent and needs its own no-follow proof.
