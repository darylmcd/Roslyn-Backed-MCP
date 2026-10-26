# changed-format-linux-path-identity — Preserve case-distinct paths in the changed-file format gate

**row:** `changed-format-linux-path-identity` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-changed-format.ps1:122` (root prefix), `:180` (changed-set deduplication), `:236` (finding membership), `:147` / `:238` (case-insensitive baseline/observed bucket maps)
- `tests/RoslynMcp.Tests/Skills/ChangedFormatGateScriptTests.cs`

## Acceptance

- [ ] Use filesystem-appropriate identity for root boundaries, changed-set deduplication diagnostic membership and baseline/observed buckets; case-distinct Linux files remain distinct while Windows behavior stays correct.
- [ ] Observe a process-level regression fail on old code with two case-distinct paths and different formatter findings; prove each changed file is independently gated and an unchanged case sibling cannot conceal or add findings.
- [ ] Preserve exact baseline multiplicity, diagnostic parsing, rename, restore and failure behavior; no baseline increase or suppression.

## Evidence

- CI prerequisite plan review found unconditional OrdinalIgnoreCase and case-insensitive Sort-Object -Unique at the anchors. Re-read in this session; separate pre-existing path-identity mechanism, not repaired by moving quality-job scheduling.
- Existing path-identity-ordinal-ignorecase-services owns C# services; repository-dotnet-format-baseline-partition owns baseline inventory. Neither tracks this script identity defect.
