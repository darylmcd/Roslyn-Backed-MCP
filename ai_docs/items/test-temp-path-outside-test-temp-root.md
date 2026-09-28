# test-temp-path-outside-test-temp-root — Decide and enforce the TestTempRoot isolation rule

**row:** `test-temp-path-outside-test-temp-root` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/prompts/backlog-sweep-addenda.md:80`
- `tests/RoslynMcp.Tests/StructuredCallToolFilterAutoLoadTests.cs:21`
- `tests/RoslynMcp.Tests/TestInfrastructure/TestTempRoot.cs`

## Acceptance

- [ ] Decide which rule is true: (a) every test temp path must live under `TestTempRoot.Current`, or (b) only paths that share the `RoslynMcpTests` parent must, with GUID-unique per-test dirs under `Path.GetTempPath()` allowed.
- [ ] If (a): add a mechanical guard (banned-API entry or source-scan test over `tests/**`) and file sized migration rows for the ~49 existing call sites; migrate `StructuredCallToolFilterAutoLoadTests` as the first.
- [ ] If (b): reword the addenda rationale so it no longer says "Every temp path", keeping the `RoslynMcpTests` re-derivation ban.
- [ ] No rule stays documented without enforcement.

## Evidence

- HEAD 6f31f065 `StructuredCallToolFilterAutoLoadTests.cs:21`: `_root = Path.Combine(Path.GetTempPath(), "rmcp-autoload-" + Guid.NewGuid().ToString("N"));`.
- Addenda `backlog-sweep-addenda.md:80`: "Every temp path is built under `TestTempRoot.Current`"; the regression-guard note at :100 bans only re-deriving `Path.GetTempPath()` + `"RoslynMcpTests"`.
- `rg -l "Path.GetTempPath\(\)" tests/RoslynMcp.Tests` lists 50 files (49 excluding `TestTempRoot.cs`), so the "every temp path" wording is not the practice and nothing enforces it.
- The cited file uses a GUID-unique directory it deletes itself, so it does not reproduce the shared-parent race; the defect is the unenforced, over-broad rule. Candidate type: test-infrastructure; filed investigate-first.

Source: backlog-remediate 20260926T234932Z follow-up.
