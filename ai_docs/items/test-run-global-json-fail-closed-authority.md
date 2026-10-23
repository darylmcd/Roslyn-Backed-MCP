# test-run-global-json-fail-closed-authority - Resolve test runner configuration within current authority

**row:** `test-run-global-json-fail-closed-authority` · **pri:** `High` · **size:** `M`

## Anchors

- src/RoslynMcp.Roslyn/Helpers/DotnetTestModeResolver.cs:20-69
- src/RoslynMcp.Roslyn/Services/TestRunnerService.cs
- src/RoslynMcp.Roslyn/Services/PhysicalGraphStage.cs

## Acceptance

- [ ] Resolve nearest global.json through one shared current-request-authorized discovery and reader; approve candidates before explicit metadata/content IO.
- [ ] Malformed, unreadable and unauthorized configuration yields a safe typed public refusal; never silently select VSTest.
- [ ] Establish genuine no-config default only after a complete authorized search. At an unauthorized ancestor, explain explicit owned configuration or authorization; do not widen roots.
- [ ] Bind inherited configuration to staged/final execution input identity. Preserve comments/trailing commas and actual native MTP/VSTest routing.
- [ ] Observe old behavior fail controls for malformed/unreadable/narrowed-boundary context and preserve authorized default/configuration positives. Record published next-major migration.

## Evidence

- Live source review 2026-10-16: resolver scans ancestor File.Exists then File.OpenRead without current policy and catches IOException, UnauthorizedAccessException and JsonException to return false.
- Exposed by restoration execution identity: global.json controls runner and SDK context but was not consistently authorized or frozen. Actual runtime proof pending.

## Context

- Forced companion of restore-callers-missing-packages-path; fix in the same complete restoration delivery.
