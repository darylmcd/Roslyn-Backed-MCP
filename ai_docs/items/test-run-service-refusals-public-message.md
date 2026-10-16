# test-run-service-refusals-public-message — Publish safe test-run operation refusals

**row:** `test-run-service-refusals-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:99`
- (new) `tests/RoslynMcp.Tests/TestRunnerPublicRefusalsTests.cs`
- `tests/RoslynMcp.Tests/ValidationToolsErrorWireTests.cs`

## Acceptance

- [ ] Classify non-test-project and no-test-project caller refusals as typed path-safe public or explicitly redacted detail; preserve published category and BCL exceptionType.
- [ ] Prove actionable service and raw-wire responses for safe inputs, hostile project names and workspace identifiers, without raw request text or absolute-path disclosure.
- [ ] Keep valid test_run behavior unchanged; exercise both refusal branches with red-first tests.

## Evidence

- Source re-read at main337fc2cde10eca3f591cc016c146dee7b893e26a and PR1779: TestRunnerService.cs:99 constructs `throw new InvalidOperationException(` followed by `$"Project '{projectName}' is not a test project. "` and available project names.
- At :106 the same plain exception contains `$"No test projects found in workspace '{workspaceId}'. "` plus test-SDK guidance. Authored caller messages lack the public-message marker; source-derived finding, no dedicated runtime reproduction yet.
- Semantic dedup across current backlog/items found package isolation, zero-match, no-build properties and timer rows, each a different mechanism.

## Context

- Independent debt observed while verifying primary-constructor remediation; not introduced or made reachable by PR1779.
