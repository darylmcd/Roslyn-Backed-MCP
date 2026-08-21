# expanded-surface-integration-test-concern-split — Split expanded-surface integration tests by tool concern

**row:** `expanded-surface-integration-test-concern-split` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ExpandedAnalysisSurfaceIntegrationTests.cs` — add focused analysis/tool coverage.
- `tests/RoslynMcp.Tests/ExpandedEditSurfaceIntegrationTests.cs` — add focused edit/path-boundary coverage.

## Acceptance

- [ ] Separate analysis/tool and edit/path-boundary scenarios into focused suites with shared setup extracted once.
- [ ] Preserve every existing assertion and test intent.
- [ ] The split suites run together without order dependence or duplicated workspace fixtures.

## Evidence

- The existing integration suite is over 1,100 lines and spans unrelated tool families; production host-tool extraction rows do not explicitly accept test-suite decomposition.
