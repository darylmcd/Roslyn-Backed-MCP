# preview-token-apply-route-provenance — Bind preview tokens to compatible apply routes

**row:** `preview-token-apply-route-provenance` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Core/Services/IPreviewStore.cs`
- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ApplyWithVerifyTool.cs`
- `src/RoslynMcp.Host.Stdio/Tools/RefactoringTools.cs`
- `tests/RoslynMcp.Tests/ApplyUndoWorkflowServiceTests.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Store a producer/workflow discriminator with each preview token.
- [ ] Each named apply route accepts only compatible token kinds; the generic verified route documents and enforces its supported set.
- [ ] Wrong-route redemption fails before mutation with an actionable error naming the expected route.
- [ ] Add one cross-route token regression proving an unrelated named apply tool cannot redeem a valid preview token.

## Evidence

- The shared preview store currently permits unrelated named apply routes to redeem tokens without producer provenance.
