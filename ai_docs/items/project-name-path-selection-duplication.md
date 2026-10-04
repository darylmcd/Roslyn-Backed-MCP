# project-name-path-selection-duplication — Centralize project name/path selection

**row:** `project-name-path-selection-duplication` · **pri:** `Low` · **size:** `M` · **deps:** `file-create-and-scaffold-refusals-public-message,cross-project-public-refusals-echo-input`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileOperationService.cs:186-191`
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:336-341`
- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs:203-206`
- `tests/RoslynMcp.Tests/FileOperationIntegrationTests.cs`
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`
- `tests/RoslynMcp.Tests/ScaffoldingIntegrationTests.cs`

## Acceptance

- [ ] Extract one shared name-or-path selection helper; probe and replace every copy of this same predicate. Preserve existing comparison, solution iteration/first-match order and caller-specific failure behavior.
- [ ] Keep request-specific error publication at each caller; the shared selection mechanism must not flatten InvalidOperation or fallback-to-empty contracts.
- [ ] Name/path keys are explicit required domain inputs. SingleTestScaffolder currently has distinct testProjectName and testProjectFilePath keys; do not collapse that distinction or add optional compatibility arguments.
- [ ] Exercise name-only, path-only, conflicting-match iteration order, case comparison and missing-project service behavior through existing meaningful integration targets; preserve the scaffolding fallback search.

## Evidence

- Current-session predicate probe 2026-10-04 found the same two-key name/path OR-selection mechanism in FileOperationService:188-190, CrossProjectRefactoringService:338-340 and SingleTestScaffolder:203-205.
- File/cross-project wrappers differ only in their refusal carrier; scaffolding uses distinct keys and returns an empty candidate list on a miss. Those caller contracts remain separate from the duplicate lookup algorithm.

## Context

- Expected production scope: the three consumers plus one shared helper; tests: existing three integration classes. Probe before implementing, and record any additional exact copies.
- Sequence after the public-message corrections to avoid conflating error-policy fixes with this independent duplication cleanup.
