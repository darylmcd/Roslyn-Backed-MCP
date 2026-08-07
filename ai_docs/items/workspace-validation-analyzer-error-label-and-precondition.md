# workspace-validation-analyzer-error-label-and-precondition — consolidated low-severity cleanup in ComputeOverallStatus

**row:** `workspace-validation-analyzer-error-label-and-precondition` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceValidationService.cs:510-511` (`ComputeOverallStatus`)
- `src/RoslynMcp.Roslyn/Contracts/IWorkspaceValidationService.cs:53` (`OverallStatus` XML doc)
- `ai_docs/domains/tool-usage-guide.md:57`
- `skills/refactor-loop/SKILL.md:95`

## Acceptance

- [ ] `analyzer-error`'s XML doc (or the docs enumerating the status set) is updated to note it now covers uncorroborated compiler-category rows, not just CA*/IDE* analyzer rules — OR the status value is split so the label stays analyzer-rule-specific.
- [ ] `ComputeOverallStatus`'s `errors` parameter documents its `Severity == "Error"`-pre-filtered precondition (or the method defensively filters internally) so a future second call site cannot turn warnings into an `analyzer-error` verdict.

## Evidence

- Code-quality review of PR #1140 (`validate-workspace-compiler-category-status-mismatch`): (1) `analyzer-error` is now also returned for `Category=="Compiler"` rows, but docs enumerating the status set still read as if it means analyzer-rule-only. (2) Widening branch 2 to `errors.Count > 0` makes `ComputeOverallStatus` fully category/severity-blind, silently depending on the single existing call site's pre-filtering with no documented precondition.

## Context

Spin-off from the `validate-workspace-compiler-category-status-mismatch` initiative (backlog-sweep plan `20260805T222513Z_backlog-sweep`, PR #1140). Both findings are documentation/defensive-coding hygiene, not functional bugs; consolidated per the sweep's filing gate (no standalone rows for low cosmetic items).
Also fix a second XML-doc inversion in the same class: IWorkspaceValidationService.cs:53 (OverallStatus, timeout sentence) and :75 (TestRunResult, "Populated only when runTests=true; otherwise null") both contradict CreateTimeoutResult (WorkspaceValidationService.cs:929, no runTests parameter) — its 3 unconditional catch-handler call sites populate TestRunResult.FailureEnvelope with ErrorKind=="Timeout" even when runTests=false, per WorkspaceValidationTimeoutTests.cs:78. [source: PR #1187 code-quality review]
