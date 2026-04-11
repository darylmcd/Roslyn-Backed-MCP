# Test Suite Audit - 2026-04-06

<!-- purpose: Cross-cutting test-suite audit covering performance smells, workspace/init issues, SRP, and tight focus. -->

## Scope

- Reviewed the full test project under `tests/RoslynMcp.Tests/`.
- Executed `dotnet test RoslynMcp.slnx --nologo`.
- Current suite state: 210 passed, 0 failed, 0 skipped, test duration 120.6s, overall command duration 127.2s.
- Reuse vs isolation snapshot:
  - `GetOrLoadWorkspaceIdAsync(...)`: 18 call sites reusing the shared sample workspace.
  - `CreateSampleSolutionCopy()`: 41 call sites creating isolated on-disk copies of the sample solution.

## Findings

### 1. High | SRP, shared state, suite throughput

**Where**

- `tests/RoslynMcp.Tests/TestBase.cs:9`
- `tests/RoslynMcp.Tests/TestBase.cs:73`
- `tests/RoslynMcp.Tests/TestBase.cs:109`
- `tests/RoslynMcp.Tests/TestBase.cs:266`
- `tests/RoslynMcp.Tests/TestBase.cs:298`
- `tests/RoslynMcp.Tests/TestBase.cs:323`
- `tests/RoslynMcp.Tests/AssemblyInfo.cs:3`
- `tests/RoslynMcp.Tests/AssemblyCleanup.cs:6`

**What**

`TestBase` is still the test suite's central god fixture. It owns MSBuild initialization, a large static service graph, workspace lifetime, shared workspace caching, repository path discovery, sample solution copy helpers, and final assembly cleanup. The test assembly is also globally marked `[DoNotParallelize]`, and per-class `DisposeServices()` calls are intentionally no-ops.

**Why it matters**

- Every test class that inherits from `TestBase` is coupled to the same static runtime state.
- One change to shared service construction or workspace behavior can fan out into broad, hard-to-localize failures.
- The non-parallel assembly setting keeps the suite stable, but it also means every test pays for serialized execution even when the test itself is read-only and parallel-safe.
- Raising `MaxConcurrentWorkspaces` to 64 in tests avoids earlier lock contention, but it also shows the fixture has become responsible for suite-wide capacity planning.

**Recommendation**

- Keep the recent workspace-cache mitigation, but split responsibilities now that the root contention issue is understood.
- Extract a `TestServiceContainer` for service construction/lifetime, a `SharedWorkspaceFixture` for cached sample-solution loading, and a `TestFixtures` helper for path/copy utilities.
- Move tests that only validate JSON shape, prompt/resource output, or single-service behavior off the global fixture where possible.
- Revisit assembly-wide `DoNotParallelize` after the shared-state surface shrinks.

**Tracking**

- This aligns with the existing backlog row `testbase-srp-split`.

### 2. Medium | Duplicate I/O-heavy copy/load loops across mutation suites

**Where**

- `tests/RoslynMcp.Tests/TestBase.cs:323`
- `tests/RoslynMcp.Tests/TestBase.cs:354`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs:22`
- `tests/RoslynMcp.Tests/ProjectMutationIntegrationTests.cs:225`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:301`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:391`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs:21`
- `tests/RoslynMcp.Tests/OrchestrationIntegrationTests.cs:99`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:21`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs:101`

**What**

The suite now reuses the shared sample workspace well for read-only integration coverage, but mutation-style tests still repeat the same expensive harness over and over: copy the full sample solution tree to `%TEMP%`, load the copied workspace, mutate files on disk, then recursively delete the copied tree.

`ProjectMutationIntegrationTests` is the clearest example: eight tests each create a fresh solution copy and load a fresh workspace. The same pattern shows up in `IntegrationTests`, `OrchestrationIntegrationTests`, `CrossProjectRefactoringIntegrationTests`, `FileOperationIntegrationTests`, `TypeExtractionTests`, `TypeMoveTests`, `UndoIntegrationTests`, and others.

**Why it matters**

- Full-directory copies are expensive compared with the actual assertions in many of these tests.
- The setup boilerplate dominates the test bodies, which increases maintenance cost and hides the true intent of each test.
- Cleanup is best-effort recursive deletion, so file handle timing problems can leave temp trees behind.
- The suite is stable today, but this pattern is the main reason growth will push runtime upward.

**Recommendation**

- Introduce a per-class copied-workspace fixture for mutation-heavy suites, with explicit reset helpers for the few files each test actually changes.
- Keep full isolated copies for cross-project and graph-shape tests, but avoid paying that cost when only one project file or one source file changes.
- Consolidate the repeated `copy -> load -> mutate -> delete` harness into one helper per concern instead of open-coded copies in many classes.

### 3. Medium | Kitchen-sink integration classes blur concerns and widen failure radius

**Where**

- `tests/RoslynMcp.Tests/IntegrationTests.cs:7`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:25`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:100`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:227`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:301`
- `tests/RoslynMcp.Tests/IntegrationTests.cs:391`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:11`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:29`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:70`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:234`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:262`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:282`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:348`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:394`

**What**

`IntegrationTests` mixes workspace metadata, symbol search, navigation, diagnostics, and refactoring preview/apply coverage in one class. `ExpandedSurfaceIntegrationTests` mixes JSON contract checks, prompt/resource validation, repo-solution analysis, edit application, and coverage-tool orchestration.

**Why it matters**

- Failures become harder to triage because the class is organized around history, not one behavior area.
- Broad classes encourage broad class initialization and make it harder to reason about which setup is actually necessary.
- The suite loses a clean separation between fast contract tests and heavier end-to-end behavior tests.

**Recommendation**

- Split `IntegrationTests` into smaller classes such as workspace-core, symbol-navigation, diagnostics, and preview-apply.
- Split `ExpandedSurfaceIntegrationTests` into tool-contract, resources-prompts, edit-apply, and heavy repo-solution/process coverage.
- Keep each class on one setup pattern so the cost of its fixture is obvious.

### 4. Medium | External and process-heavy tests are mixed into the default lane without explicit categorization

**Where**

- `tests/RoslynMcp.Tests/NuGetVulnerabilityScanIntegrationTests.cs:19`
- `tests/RoslynMcp.Tests/NuGetVulnerabilityScanIntegrationTests.cs:21`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:234`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:237`
- `tests/RoslynMcp.Tests/ExpandedSurfaceIntegrationTests.cs:394`
- `tests/RoslynMcp.Tests/SecurityDiagnosticIntegrationTests.cs:22`
- `tests/RoslynMcp.Tests/SecurityDiagnosticIntegrationTests.cs:225`
- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:85`
- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:93`

**What**

Several tests exercise heavier boundaries than the default sample-workspace path:

- `ScanNuGetVulnerabilitiesAsync_ReturnsStructuredResult` depends on a vulnerability scan path that shells out and can hit NuGet infrastructure.
- `Reflection_And_Di_Analysis_Run_On_Repo_Solution` loads the repository solution itself instead of the small sample fixture.
- `TestCoverageTool_Returns_Structured_Response` drives coverage execution through the tool surface.
- `SecurityDiagnosticIntegrationTests` loads a second solution and reruns analyzer-backed checks over it.
- `PerformanceBaselineTests` intentionally perform wall-clock checks with real workspace loads.

**Why it matters**

- These tests are valid, but they are materially slower and more environment-sensitive than the rest of the suite.
- They raise the floor for every local default test run.
- When one of them flakes, the failure mode looks like a normal integration failure even though the root cause is usually process, network, analyzer availability, or broader machine state.

**Recommendation**

- Add explicit categories such as `Network`, `Process`, `RepoSolution`, and `Performance`.
- Keep them in full validation, but allow fast local runs and focused CI lanes to exclude them when appropriate.
- The NuGet vulnerability scan test already has a matching backlog item (`vuln-scan-network-mock`); the other heavy categories are still untracked.

### 5. Low/Medium | One test still uses arbitrary delay for synchronization

**Where**

- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:65`
- `tests/RoslynMcp.Tests/PerformanceBehaviorTests.cs:84`

**What**

`Validation_Service_Serializes_Commands_For_The_Same_Workspace` correctly uses a controllable fake runner for the long-running command, but it still pauses for 150ms before asserting that only one command is running.

**Why it matters**

- Fixed delays are fragile on slow agents and wasteful on fast ones.
- The test already owns the fake runner, so a deterministic signal is available with a small helper change.

**Recommendation**

- Replace the delay with an explicit second-invocation signal or queue-entered signal emitted by `BlockingDotnetCommandRunner`.
- Keep the test in this class; only the synchronization mechanism needs to change.

### 6. Low | Performance baseline tests are useful smoke checks, but they are coarse and environment-sensitive

**Where**

- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:20`
- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:85`
- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:93`
- `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs:96`

**What**

The current budgets are intentionally generous: 10 seconds for symbol search and reference search, 15 seconds for complexity metrics, and 30 seconds for unused symbol scan and workspace load. They catch catastrophic regressions, not nuanced drift.

**Why it matters**

- They are not wrong, but they are noisy as default-lane assertions because the signal is wall-clock time on a mutable local machine.
- Their value is much higher as a categorized smoke gate than as an always-on precise performance detector.

**Recommendation**

- Keep these tests, but treat them as performance smoke coverage.
- If the suite grows further, move them behind a dedicated category or validation lane rather than tightening the thresholds in the default lane.

## Positive Notes

- The suite currently passes end to end; this audit found structural and cost risks, not active failures.
- The shared sample-workspace cache in `GetOrLoadWorkspaceIdAsync(...)` is a real improvement over the old repeated-load pattern.
- Mutation tests generally do the right thing by operating on isolated copies rather than mutating the shared sample fixture in place.
- No `Thread.Sleep(...)` usage was found in the test project.

## Suggested Follow-Up Work

If maintainers want to track the unaddressed items from this audit, the cleanest backlog candidates are:

- `test-suite-parallel-lane-split`
- `test-suite-heavy-fixture-reuse`
- `test-suite-remove-fixed-delay`