# workspace-validation-platform-path-comparison — Preserve case-distinct validation paths

**row:** `workspace-validation-platform-path-comparison` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs` — ResolveChangedFiles uses OrdinalIgnoreCase for deduplication and workspace document membership on every OS.
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs` — isolated Git workspace validation scope coverage.

## Acceptance

- Use the repository platform-aware path comparison policy for changed-file deduplication and workspace-document membership.
- On a case-sensitive filesystem, two distinct changed C# files differing only by case remain in the validation scope; a wrong-case path is not treated as a known document.
- Add a case-distinct-path regression that explicitly skips when the fixture filesystem is case-insensitive; preserve Windows comparison behavior.

## Evidence

2026-09-15 direct source review: ResolveChangedFiles applies Distinct(StringComparer.OrdinalIgnoreCase), then builds workspacePaths with the same unconditional comparer. This loses one of two case-distinct source paths on Linux before test discovery and compilation scoping.
