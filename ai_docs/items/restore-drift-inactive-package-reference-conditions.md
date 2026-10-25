# restore-drift-inactive-package-reference-conditions — Compare active evaluated package expectations

**row:** `restore-drift-inactive-package-reference-conditions` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:287` — CollectExpectedPackages through329; central raw-version expectations at371 onward.
- `src/RoslynMcp.Roslyn/Helpers/MsBuildMetadataHelper.cs` — existing explicit-operation-global native evaluation seam.
- `tests/RoslynMcp.Tests/RestoreStalenessDetectorTests.cs`

## Acceptance

- [ ] Derive expected PackageReference items and central requirements from actual active evaluation under operation globals, including item/group/import conditions.
- [ ] Observe RED when an inactive package falsely requires restore; cover the opposite configuration, active version drift and imported/central conditions as controls.
- [ ] Preserve active references, version updates and sanitized unreadable/malformed metadata classification. Cover every same-mechanism raw expectation reader without an independent condition interpreter.
- [ ] Complete fresh review, affected and hosted qualification before closure; split only into independently correct rows if needed.

## Evidence

- Main58ba4207 source enumerates raw XML Descendants(PackageReference), Include/Update and versions without evaluating conditions. The false-restore consequence is inferred, not runtime-reproduced.
- Landed metadata-global propagation does not evaluate raw package expectations; this is a distinct mechanism.

## Context

- Independent nonblocking remainder; no dependency from current restore closure. Re-derive use of the existing evaluator rather than adding native-authority machinery.
