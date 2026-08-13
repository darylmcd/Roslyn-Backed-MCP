# path-boundary-link-swap-toctou — Close validation-to-use link-swap race

**row:** `path-boundary-link-swap-toctou` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs`
- `src/RoslynMcp.Host.Stdio/Tools/EditTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`

## Acceptance

- [ ] Design the boundary API so downstream filesystem consumers use the validated canonical target or perform a final revalidation immediately before mutation.
- [ ] Apply the primitive first to the highest-risk direct file/project write tools without weakening configured-root or sibling-worktree behavior.
- [ ] Keep the server-owned boundary authoritative after validation and during use.
- [ ] Add one deterministic link/junction swap regression proving a path cannot be redirected outside the configured boundary between validation and write.

## Evidence

- Validation currently canonicalizes only for comparison and returns no canonical target; downstream tools retain the original mutable link path.
