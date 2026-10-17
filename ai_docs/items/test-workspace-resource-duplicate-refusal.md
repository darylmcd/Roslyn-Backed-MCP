# test-workspace-resource-duplicate-refusal — Consolidate identical unknown-workspace resource assertions

**row:** `test-workspace-resource-duplicate-refusal` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs:134`
- `tests/RoslynMcp.Tests/FetchMcpResourceReadinessTests.cs:138`

## Acceptance

- [ ] Retain one unknown-workspace direct-resource regression with explicit service initialization; remove only the same-input/same-entry-point duplicate.
- [ ] Keep real protocol-era error-frame tests and readiness/reload behavior tests intact.
- [ ] Verify both classes and preserve the WorkspaceNotFoundException contract; record the intentional one-case reduction.

## Evidence

- AST bodies are identical: same shared gate/manager, same unknown ID and same exact WorkspaceNotFoundException assertion.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
