# test-fixture-filesystem-path-normalization — TestFixtureFileSystem path handling defect

**row:** `test-fixture-filesystem-path-normalization` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/TestFixtureFileSystem.cs:84`

## Acceptance

- [ ] DeleteDirectoryIfExists and sibling helpers normalize paths (extended-length prefix, trailing dot/space, drive alias) the same way NoFollowDirectoryDeleter does, or delegate to it.
- [ ] A regression test with a descendant junction and a trailing-dot name fails on the old helper.

## Evidence

- Found during the PR 1964 review: fixture cleanup leaked a run directory under the temp RoslynMcpTests root when a test left a descendant junction; BCL recursive delete throws UnauthorizedAccess on it.
