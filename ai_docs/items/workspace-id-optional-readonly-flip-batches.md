# workspace-id-optional-readonly-flip-batches — Flip the remaining read-only tools to optional workspaceId, per-file batches

**row:** `workspace-id-optional-readonly-flip-batches` · **pri:** `Low` · **size:** `—` · **deps:** `workspace-id-optional-adoption-evidence,workspace-id-optional-named-argument-prerequisite`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs` (representative; exact file set fixed when this row is decomposed)
- `src/RoslynMcp.Host.Stdio/Tools/SymbolTools.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`

## Acceptance

- [ ] Each flipped method gets a `[Description]` invite-omission update plus the residual-case `RequireResolvedWorkspaceId` guard, mirroring the `SymbolTools.cs` pilot pattern.
- [ ] Per swept file, a reflection test asserts its read-only tools expose `workspaceId` `Required:false` while mutating tools keep `Required:true`; the guard throw-branch is covered.
- [ ] CHANGELOG "Changed" + ADR-lite — published surface, backward-compatible/additive (Directive #4).

## Evidence

Measured on `main` 2026-09-02: **121 read-only tools across 43 `Tools/*.cs` files still take a REQUIRED `workspaceId`** — materially more than the parent row's "~45 read-only tools" estimate. Largest: `SymbolTools` 17, `AdvancedAnalysisTools` 12, `ProjectMutationTools` 10, `AnalysisTools` 8, `RefactoringTools` 6, `WorkspaceTools` 6.

## Context

Split from `workspace-id-optional-readonly-surface-full-sweep` (2026-09-02).

**Size deliberately `—`: not decomposable yet, on purpose.** 43 files at the Rule 3 cap would be ~11 children, and every one of them would be dep-blocked behind the adoption gate. Decomposing before the go/no-go would create eleven blocked rows whose batch boundaries the gate's answer may change. Split into per-file batches once `workspace-id-optional-adoption-evidence` returns "go".

**Parent's stale estimate corrected:** the row said "~45 read-only tools"; the live count is 121 across 43 files. Whoever plans the batches should re-measure rather than trust either number.

**Sub-batch by `*Tools.cs` file, at most one catalog-touching initiative per wave.** Backward-compatible (explicit-id callers unaffected) so additive, but published surface, so CHANGELOG "Changed" + ADR-lite.
