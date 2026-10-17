# test-single-project-refactoring-fixtures — Use a minimal owned project for single-project refactoring cases

**row:** `test-single-project-refactoring-fixtures` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs:2478`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs:35`
- `tests/RoslynMcp.Tests/Helpers/SingleProjectWorkspaceFixture.cs (new)`

## Acceptance

- [ ] Replace the misleading SetupSingleProjectFixtureAsync full-solution copy/load with an owned single-project fixture for genuinely single-project cases.
- [ ] Preserve actual MSBuild references, language options, unsafe/nullable settings, compile/emit/run checks and preview/apply behavior; retain multi-project fixtures wherever caller/project discovery is the subject.
- [ ] Share fixture construction, keep each mutable scenario isolated, close workspace before deleting files, and clean up failed initialization.
- [ ] Measure repeated setup/case durations on both OSes and preserve all existing semantic assertions; do not make broad mutable fixture reset a prerequisite.

## Evidence

- ParameterObject helper copies the three-project SampleSolution and loads its solution despite its name; signature cases repeatedly add a single self-contained source to that same large fixture.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
