# change-signature-unrelated-invocation-rewrite — Limit change_signature add/remove/reorder to the target call sites

**row:** `change-signature-unrelated-invocation-rewrite` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:53`
- `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Red first on SampleSolution: `change_signature_preview(op=add, metadataName=SampleLib.AnimalService.CountAnimals, name=strict, parameterType=bool, defaultValue=false)` must leave `Assert.AreEqual(2, count);` in `SampleLib.Tests/AnimalServiceTests.cs` untouched (today it becomes an explicit named-argument generic call).
- [x] Red first on this solution: `op=add` on `RoslynMcp.Roslyn.Helpers.PhysicalPathResolver.CanonicalizeDriveRoot` returns a preview instead of "An existing argument has no original semantic parameter binding." Done in PR1 (#2173, merged 2026-11-20T23:48:38Z) via the shared `ArgumentOperationResolver`.
- [ ] Only invocations bound to the target symbol are REWRITTEN; unrelated caller-info calls keep their text unless a compile-driven drift check shows a materialized caller-info constant changed, and then only the drifted parameters are frozen. Call-node binding evidence stays for all invocations (guards unrelated overload rebinding; pinned by `CrossDocument_DefaultStructAndUnrelatedOverloadStayBound`). Cover add, remove and reorder.
- [x] Failed-preview latency moved to row `change-signature-preview-latency` (measured 2026-11-21 at f7016d6c: refusal 56.3 s, success 67.2 s on RoslynMcp.slnx).

## Evidence

- P6-F8: 11/11 add/remove/reorder calls failed on RoslynMcp.slnx across five methods, including on a fresh server process.
- P18-F1: op=add on SampleSolution rewrote an unrelated MSTest assertion (caller-argument-expression parameters materialized).
- Finding id and full call evidence: audit-reports/20261120T171538Z_roslynmcp_mcp-server-surface-test.md (local, git-ignored).

## Context

- Both symptoms point at the same mechanism (argument binding walked for invocations that are not call sites of the target); that link is inferred from the throw site, not proven.

## Notes

### PR2 plan (reviewed design, 2026-11-20)

PR1 (#2173) landed `src/RoslynMcp.Roslyn/Helpers/ArgumentOperationResolver.cs` (context only; consumed by `ChangeSignatureAddRemovePreviewBuilder.cs` and `BulkRefactoringService.cs`). Open: acceptance bullets 1, 3, 4.

- Build the candidate without freezing unrelated calls.
- Compile-driven drift check on the FINAL candidate (after `AddAliasDirectives` and `AddAliasDirectivesToScope`): read caller-info constants from `IArgumentOperation.Value.ConstantValue` for unrelated caller-info calls in changed documents only.
- Minimal `param: literal` freeze only for drifted caller-info parameters. Never use `NameOriginalArguments` / `RetainTypeSubstitution` for them.
- The gate at `ChangeSignatureAddRemovePreviewBuilder.cs:428` becomes `editedCall` only; delete the `:466-470` cleanup.
- Call-node binding evidence is KEPT for all invocations: the overload-rebinding refusal is pinned by `CrossDocument_DefaultStructAndUnrelatedOverloadStayBound`. This is a stated deviation from bullet 3's literal wording ("only invocations bound to the target are validated").
- When an unrelated call's `BindingTag` is missing in the candidate: skip if its containing argument was removed, refuse if unexplained.
- Tests: `Assert.AreEqual(2, count)` byte-identical on `CreateLibraryTestsWorkspace`; `Log.Tag(Compute(1,2))` drift frozen with only that parameter named; multi-line removal shifting `Line()`; alias-only line shift; `MemberName` / struct construction; `CallerFilePath` after a struct move.
- Latency: success path measured 53-73 s on `RoslynMcp.slnx` after PR1 (baseline compile `:297` + candidate compile `:1191` + per-document bind; method: throwaway harness with `RealWorkspaceLifecycleFixture` + `Stopwatch`). This exceeds the 30 s yardstick (`skills/mcp-server-surface-test/prompts/phases/output-and-close.md:337`). Decide in PR2 whether short-circuiting pre-checks before `:297` is possible; otherwise file a perf row. Hard cap: `ExecutionGateOptions.RequestTimeout` 2 min.
