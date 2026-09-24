# root-sample-solution-tree-duplicate — Remove the stale root-level SampleSolution tree

**row:** `root-sample-solution-tree-duplicate` · **pri:** `Low` · **size:** `M`

## Anchors

- `SampleSolution.slnx`
- `SampleApp/Program.cs`
- `SampleLib/AnimalService.cs`
- `SampleLib.Tests/AnimalServiceTests.cs`

## Acceptance

- [ ] Root SampleApp/, SampleLib/, SampleLib.Tests/ and SampleSolution.slnx removed (19 tracked files)
- [ ] Workspace auto-discovery at the repo root finds only the main server solution
- [ ] Full test suite still passes (tests use samples/SampleSolution copies)

## Evidence

- git ls-files → 19 tracked files last changed in 157fdded (1.14.0); root Program.cs differs from samples/SampleSolution copy; no eng/.github/justfile references; 2026-08-25 logging audit run2 stdout shows 2 candidate solutions (RoslynMcp.slnx, SampleSolution.slnx). — see `ai_docs/audits/20260924-1305/report.md` (check C6) and `ai_docs/audits/20260924-1305/findings.json`
