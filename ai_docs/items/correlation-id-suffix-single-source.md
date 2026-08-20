## Anchors

- `src/RoslynMcp.Roslyn/Services/FlowAnalysisFailurePolicy.cs:28`
- `src/RoslynMcp.Roslyn/Services/ScaffoldingReadFailurePolicy.cs:17`
- `src/RoslynMcp.Core/Services/PublicExceptionDetailPolicy.cs:15`

## Acceptance

- A shared helper in `RoslynMcp.Core` formats the `correlationId=<id>` suffix; `FlowAnalysisFailurePolicy` and `ScaffoldingReadFailurePolicy` call it instead of interpolating the tail themselves.
- A test asserts the shared suffix format so drift between call sites is caught.

## Evidence

Found in the Step 8b code-quality review of PR #1297. `correlationId=` is hand-interpolated in 12 files (13 occurrences); PR #1297 added the newest copy with the identical `$"... correlationId={detail.CorrelationId}"` shape as `ScaffoldingReadFailurePolicy`.

The shape is pre-existing and this diff mirrors it correctly — each new copy is another drift point rather than a defect today. Worth noting that this sweep alone added two of the copies (`ScaffoldingReadFailurePolicy` via PR #1286, `FlowAnalysisFailurePolicy` via PR #1297), so the pattern is actively spreading.

Source: code-quality review of PR #1297, sweep 20260819T180531Z.
