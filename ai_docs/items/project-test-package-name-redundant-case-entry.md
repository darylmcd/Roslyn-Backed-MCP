# project-test-package-name-redundant-case-entry - Remove redundant case-only package data

**row:** `project-test-package-name-redundant-case-entry` · **pri:** `Low` · **size:** `S` · **deps:** `project-metadata-parser-broad-exception-fallbacks`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:179` - TestPackageNames uses StringComparer.OrdinalIgnoreCase.
- `src/RoslynMcp.Roslyn/Helpers/ProjectMetadataParser.cs:182` - initializer includes both NUnit and nunit.
- `tests/RoslynMcp.Tests/ProjectMetadataParserIsTestProjectTests.cs` - package-based test-project recognition controls.

## Acceptance

- [ ] Remove the redundant case-only initializer entry while preserving the case-insensitive comparer and all distinct package IDs.
- [ ] Verify existing package-recognition coverage retains mixed-case NUnit recognition and unrelated-package negative controls. Add a meaningful behavioral case only if that coverage is missing.
- [ ] Keep this as data cleanup; do not claim a runtime classification defect or invent a source-mirroring regression test.

## Evidence

- Current source inspection during metadata failure-classification remediation found the duplicate initializer data in an OrdinalIgnoreCase set. No runtime fault is claimed; the set already ignores the second spelling.
- This is a pre-existing independent redundancy, separate from exception fallback ownership. No matching existing row was found by the implementer's backlog search.
