# tool-error-handler-argument-sniffing-arms-retired — BuildSafeArgumentMessage collapses to sanctioned-root check + IPublicMessageException + generic fallback

**row:** `tool-error-handler-argument-sniffing-arms-retired` · **pri:** `Medium` · **size:** `S` · **deps:** `argument-exception-throw-sites-lack-public-message, tool-scripting-undo-workflow-argument-refusals-public-message, symbol-locator-and-source-text-tool-argument-refusals-public-message, get-prompt-text-unknown-prompt-public-message, resource-argument-refusals-public-message, root-boundary-argument-refusals-public-message, dispatch-and-tool-argument-refusals-public-message, parameter-object-argument-refusals-public-message, change-signature-service-refusals-public-message, format-range-service-refusals-public-message, bulk-refactoring-argument-refusals-public-message, type-extraction-argument-refusals-public-message, restructure-and-semantic-grep-argument-refusals-public-message, edit-service-argument-refusals-public-message, suppression-argument-refusals-public-message, code-action-and-flow-argument-refusals-public-message, navigation-and-locator-argument-refusals-public-message, symbol-handle-and-reference-argument-refusals-public-message, workspace-lifecycle-argument-refusals-public-message, preview-token-argument-refusals-public-message, build-and-analysis-argument-refusals-public-message, scripting-and-snippet-argument-refusals-public-message, file-create-and-scaffold-refusals-public-message`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/ToolErrorHandlerParameterValidationTests.cs`
- `tests/RoslynMcp.Tests/ToolErrorHandlerSpecificityTests.cs`

## Acceptance

- [ ] BuildSafeArgumentMessage has only the sanctioned-root check, the marker check, and the generic fallback.
- [ ] Tool-level envelope messages for projectName, lineRange, pattern, catalog diff and source-location refusals are unchanged (now sourced from Public throws).

## Evidence

- 0 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~30000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
