## Anchors

- `src/RoslynMcp.Roslyn/Services/FlowAnalysisFailurePolicy.cs:13` — the single shared `Remediation` const
- `src/RoslynMcp.Roslyn/Services/ExtractMethodService.cs:166` — the call site that lost its specific guidance

## Acceptance

- `extract_method_preview` failure messages no longer advise widening to "a complete expression-bodied member" — not a valid extract-method selection — and instead carry selection/statement-block guidance.
- `FlowAnalysisFailurePolicy` takes the remediation text (or an operation-keyed variant) rather than one shared const, with a test per operation.

## Evidence

Found in the Step 8b code-quality review of PR #1297. Consolidating the three failure paths onto one shared `Remediation` const replaced `ExtractMethodService`'s prior selection-specific guidance ("Ensure the selection covers complete statements within a single block.") with flow-analysis wording whose first clause targets expression-bodied members. An expression-bodied member cannot be an extract-method selection, so that half of the advice actively misdirects extract-method callers.

This is a small user-facing behavior regression introduced by the consolidation, not a pre-existing wart: the guidance was correct before PR #1297 and is wrong for one of the three operations after it.

Source: code-quality review of PR #1297, sweep 20260819T180531Z.
