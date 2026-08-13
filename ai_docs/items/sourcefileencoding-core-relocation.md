# sourcefileencoding-core-relocation — encoding consolidation is structurally incomplete

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SourceFileEncoding.cs`
- `src/RoslynMcp.Core/Services/IUndoService.cs`

## Acceptance

- [ ] `FileSnapshotDto.FromExistingBytes` decodes through the shared encoding helper instead of constructing its own `StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true)`.
- [ ] Only one BOM-detecting `StreamReader` construction remains in `src/` (verifiable via a grep for `detectEncodingFromByteOrderMarks`).

## Evidence

Traced during the PR #1243 review: after that PR consolidated the encoding sniffing, a grep for `detectEncodingFromByteOrderMarks` across `src` STILL returns two construction sites — the new `SourceFileEncoding` helper and `FileSnapshotDto` in `RoslynMcp.Core`.

Cause is structural, not an oversight: the extracted helper landed in `RoslynMcp.Roslyn` while `FileSnapshotDto` lives in `RoslynMcp.Core`, which cannot reference upward. Closing it means relocating the helper to Core.

Pre-existing code, surfaced as a follow-up rather than a diff finding.
