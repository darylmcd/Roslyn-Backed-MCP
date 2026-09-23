# root-sample-solution-duplicates-removal — Delete the stale root sample duplicates

**row:** `root-sample-solution-duplicates-removal` · **pri:** `Low` · **size:** `L`

# root-sample-solution-duplicates-removal — Delete the stale root sample duplicates

## Anchors

- `SampleSolution.slnx`
- `SampleApp/`
- `SampleLib/`
- `SampleLib.Generators/`
- `SampleLib.Tests/`
- `RoslynMcp.slnx`

## Acceptance

- [ ] Repo root holds exactly one .slnx (RoslynMcp.slnx).
- [ ] Full test run green; CI restore of samples/SampleSolution unaffected.
- [ ] A guard test or verifier asserts a single root solution.

## Evidence

- TestFixtureFileSystem.FindFixturePath only resolves samples/<fixture>/; ci.yml restores samples/SampleSolution; audit run2-stdout.jsonl:3 reported two candidate solutions. (doc-audit 2026-09-23)
Forcing shape: one mechanical deletion of a single duplicate fixture tree (21 files, one regression shape); splitting would leave a half-deleted solution, so keep whole.
