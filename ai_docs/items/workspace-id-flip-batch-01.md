# workspace-id-flip-batch-01 — flip read-only `workspaceId` to optional (batch 1 of 11)

**row:** `workspace-id-flip-batch-01` · **pri:** `Low` · **size:** `M` · **deps:** `workspace-id-optional-adoption-evidence, workspace-id-named-args-symbol-search, workspace-id-named-args-symbol-navigation, workspace-id-named-args-expanded-surface, workspace-id-named-args-refactoring, workspace-id-named-args-shims-extraction, workspace-id-named-args-validation`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AdvancedAnalysisTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ProjectMutationTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/AnalysisTools.cs`
- `tests/RoslynMcp.Tests/WorkspaceIdOptionalBatch01Tests.cs` (new — per-batch reflection assertion)

## Acceptance

- [ ] Every read-only tool in these 4 file(s) takes `workspaceId` as OPTIONAL, with a `[Description]` update inviting omission and the residual-case `RequireResolvedWorkspaceId` guard — mirroring the `SymbolTools.cs` pilot pattern.
- [ ] A reflection test asserts this batch's read-only tools expose `workspaceId` `Required:false` while its mutating tools keep `Required:true`; the guard's throw-branch is covered.
- [ ] No tool in this batch has a REQUIRED parameter positioned after `workspaceId` (CS1737) — if one appears it is handled by `workspace-id-optional-named-argument-prerequisite`, not worked around here.

## Evidence

Measured on `main` 2026-09-02 — read-only tools in this batch still taking a REQUIRED `workspaceId`:

| File | read-only tools with required `workspaceId` |
|---|---|
| `SymbolTools.cs` | 17 |
| `AdvancedAnalysisTools.cs` | 12 |
| `ProjectMutationTools.cs` | 10 |
| `AnalysisTools.cs` | 8 |

Batch total: **47 tools across 4 file(s)**. Whole remainder at split time: **121 tools across 43 files** — materially more than the retired parent row's "~45 read-only tools" estimate.

## Context

Split from `workspace-id-optional-readonly-flip-batches` (2026-09-02), itself split from `workspace-id-optional-readonly-surface-full-sweep`.

**Dep-blocked by design** on two prerequisites: `workspace-id-optional-adoption-evidence` (the pilot's `_meta.autoResolution` go/no-go — do not sweep before it says go) and `workspace-id-optional-named-argument-prerequisite` (positional call sites converted so a later `= null` default cannot hit CS1737).

**Batching rule:** at most four `Tools/*.cs` files per batch (Rule 3 base cap), sub-batched per file as the parent required. Verify per file whether the catalog needs a per-parameter schema regeneration — the pilot found it did not, but confirm rather than assume; if it does, the catalog partial is a gate-forced companion (RMCP001/RMCP002) and the plan stanza must cite it as such.

**Published surface, backward-compatible** (explicit-id callers unaffected) so additive — but still CHANGELOG "Changed" + ADR-lite per Directive #4.

Pilot reference: `workspace-id-optional-readonly-surface-flip` (PR #959) and its code-quality review.
