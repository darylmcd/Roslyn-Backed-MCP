# test-os-guards-report-passed — OS-guarded tests return early and report Passed

**row:** `test-os-guards-report-passed` · **pri:** `Medium` · **size:** `L`

## Anchors

- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs`
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`
- `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs`

## Acceptance

- [ ] No `[TestMethod]` body starts with an `OperatingSystem.Is*` check that returns; each of the 13 uses the same not-run mechanism as the existing OS guards (`Assert.Inconclusive` with the OS named, or one shared `RequireOs` helper).
- [ ] On Windows the 8 Linux-only tests show as not executed in the TRX, not as passed; `rg "if \(!?OperatingSystem\.Is\w+\(\)\)\s*return;" tests/RoslynMcp.Tests` returns nothing.
- [ ] Also covered: the two tests that `return` when a regex finds no matches (`Skills/McpServerSurfaceTestSkillTests.cs:395` and its sibling) assert that matches exist instead.

## Evidence

- `tests/RoslynMcp.Tests/CompositeApplyOrchestratorTests.cs:157` — `if (!OperatingSystem.IsLinux()) return;` as the first statement of `AtomicFileWriter_SymlinkSwapCannotRedirectWriteOrCommit` (7 such in this file: :157, :183, :206, :232 and three more).
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:54` (4 in this file); `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs` (1); `tests/RoslynMcp.Tests/EditUndoCohesionTests.cs` (1). `rg -c` total: 13.
- Contrast: 41 OS-conditional `Assert.Inconclusive` sites in the same project report the skip.
- Found by a read-only syntactic probe of `tests/RoslynMcp.Tests` at 6cf842a8 on 2026-10-09 (ast-grep plus a string/comment mask; no test executed); counts re-derived with `rg` at e52c1241 before filing.

## Context

- regression_shape: a platform guard that ends a test as Passed.
