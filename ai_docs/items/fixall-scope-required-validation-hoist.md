# fixall-scope-required-validation-hoist — hoist fix_all scope-required parameter validation ahead of provider discovery

**row:** `fixall-scope-required-validation-hoist` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:113`
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:225`
- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:235`
- `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs:71`

## Acceptance

- [ ] `PreviewFixAllAsync` validates scope-required parameters (`projectName` for scope `project`, `filePath` for scope `document`) immediately after `ParseScope`, before code-fix-provider discovery, so the corrective `ArgumentException` is returned regardless of whether the diagnostic has a registered FixAll provider.
- [ ] A test asserts `fix_all` with `scope: "project"`, a blank `projectName`, and a diagnostic that has no FixAll provider (e.g. `CS8019`) throws the `projectName` `ArgumentException` instead of returning the "No code fix provider" guidance envelope.
- [ ] (Optional, folds in a related low finding) Convert the blank-projectName regression test to a `[DataTestMethod]` with `[DataRow(null)]`, `[DataRow("")]`, `[DataRow("   ")]` to cover the omitted/null case explicitly, not just `"   "`.

## Evidence

Traced during code-quality review of `fixall-blank-projectname-silent-wrong-target` (which added the `projectName` guard inside `ResolveTargets`, called at `FixAllService.cs:113`): `FixAllService.cs:85-94` and `:97-110` return the "No code fix provider" guidance envelope *before* `ResolveTargets` runs. `CS8019`'s lack of a registered FixAll provider in this workspace is already pinned by the existing test at `FixAllServiceIntegrationTests.cs:42-60`, so `fix_all(scope: "project", projectName: null, diagnosticId: "CS8019")` hits that early return and never reaches the new guard — same missing-parameter input, two different (and inconsistent) errors depending on the diagnostic id.

## Context

Spin-off from the `fixall-blank-projectname-silent-wrong-target` row's code-quality review (top-n-remediation run 20260810T233007Z). The mirrored `MsBuildEvaluationService` guard also appends a "Use workspace_status to list loaded projects" hint that the new message omits — cosmetic, worth folding into this row's scope rather than filing separately.
