# tool-scripting-undo-workflow-argument-refusals-public-message — evaluate_csharp timeoutSeconds, undo workspaceId/sequenceNumber and recommend_workflow task refusals are Public

**row:** `tool-scripting-undo-workflow-argument-refusals-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ScriptingTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/UndoTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/WorkflowRecommendationTools.cs`
- `tests/RoslynMcp.Tests/ErrorResponseObservabilityTests.cs`
- `tests/RoslynMcp.Tests/UndoIntegrationTests.cs`
- `tests/RoslynMcp.Tests/WorkflowRecommendationToolsTests.cs`

## Acceptance

- [ ] evaluate_csharp{timeoutSeconds:-1} states 'must be greater than 0' through the envelope.
- [ ] revert_last_apply without workspaceId names 'workspaceId'.

## Evidence

- 5 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
