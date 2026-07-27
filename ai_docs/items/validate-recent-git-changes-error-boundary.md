# validate-recent-git-changes-error-boundary — Retire the duplicate tool error boundary

**row:** `validate-recent-git-changes-error-boundary` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ValidationBundleTools.cs` (`ValidateRecentGitChanges`)
- `tests/RoslynMcp.Tests/ValidateRecentGitChangesTests.cs`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterTests.cs`

## Acceptance

- [ ] Remove the tool-local generic catch and route failures through the canonical `StructuredCallToolFilter` error envelope.
- [ ] Preserve cancellation propagation and the stable structured error shape with direct filter-level regression coverage.

## Evidence

- The 2026-07-26 extraction review confirmed this tool remains the local try/catch exception to the repository's single structured error-boundary policy.
