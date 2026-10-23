# restore-physical-project-mutation-rendering - Render physical project edits consistently

**row:** `restore-physical-project-mutation-rendering` · **pri:** `Medium` · **size:** `L` · **deps:** `workspace-restore-packages-path`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ProjectReferenceMutationRenderer.cs`
- `src/RoslynMcp.Roslyn/Services/DocumentSetPersistenceService.cs`
- `src/RoslynMcp.Roslyn/Helpers/SolutionDiffHelper.cs`
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`
- `src/RoslynMcp.Roslyn/Services/RefactoringService.cs`
- `tests/RoslynMcp.Tests/SolutionDiffHelperTests.cs`
- `tests/RoslynMcp.Tests/UndoFileOperationsTests.cs`

## Acceptance

- [ ] Bind all reference and explicit Compile operations to original physical XML nodes before edits; aggregate compatible framework contexts into one mutation per physical project file. Reject contradictory physical intents before effects.
- [ ] Preserve conditions, unrelated metadata, comments, encoding and unrelated XML; update intended aliases/embed metadata only. Preserve build-only references while adding real compilation edges.
- [ ] Use the same rendering for visible project-file diffs, token write paths and source persistence. Preserve rollback and undo and default-Compile SDK behavior.
- [ ] Observe alias swaps, overlapping context edits and build-only-to-compilation additions fail before repair; keep disjoint conditional edits, duplicate equivalent contexts, explicit Compile changes and apply/undo green.
- [ ] Fresh independent review has no blocking findings; scoped source/preview/undo checks and hosted checks pass. Close only after verified merge and the delivery batch full serialized gate.

## Evidence

- Prerequisite split from restore-callers-missing-packages-path after the third targeted review retained a multi-context alias-swap defect; source-only renderer must be correct independently before fork replay consumes it.
- Current-session immutable-node-red.log: six failures, 37 passes; physical-context-red.log: two failures, seven passes; conditional-metadata-red.log: one failure, six passes. Logs: ~/.codex/scratch/roslyn-remediate-20261023-split/.
- Own scoped checkpoint b88d8c18 preserves dependent restore/fork work. PR 1806 and stash codex-remediate-20261022-preserve-restore remain untouched reference material; no native-authority machinery belongs here.
- Scope is five production files; the public apply regression forces the bounded metadata-replacement rebase correction. The dependent restore row retains restore planning, authorization ordering, fork preparation/replay and assets readers; this row does not close that parent.
