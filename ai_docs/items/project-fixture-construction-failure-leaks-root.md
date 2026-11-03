# project-fixture-construction-failure-leaks-root — Compensate fixture creation failures

**row:** `project-fixture-construction-failure-leaks-root` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/MsBuildEvaluationCacheTests.cs` (`CreateProjectFixture`)
- `tests/RoslynMcp.Tests/ProjectOutputTypeTests.cs` (`CreateProjectFixture`)
- `tests/RoslynMcp.Tests/PublishVersionContextTests.cs` (`VersionFixture` constructor)

## Acceptance

- [ ] If writing the project file fails after creating the owned fixture root, attempt to remove that root and preserve both construction and cleanup failures.
- [ ] Red-first regressions inject project-file and props-file write failures in all three constructors and verify owned root compensation; successful fixture creation and disposal remain intact.

## Evidence

- Review of draft PR #1953 at `232e325dc61cb907761837b51e0fcced89e03f08`: both `CreateProjectFixture` helpers and the `VersionFixture` constructor call `Directory.CreateDirectory` and then `File.WriteAllText` before ownership is safely returned. A write exception leaves the directory without compensating cleanup. The construction behavior predates the fixture-disposal change.
