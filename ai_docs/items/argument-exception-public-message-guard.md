# argument-exception-public-message-guard — src-scoped BannedSymbols ban on all ArgumentException/AOORE constructors (tests/analyzers/samples exempt)

**row:** `argument-exception-public-message-guard` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory, argument-exception-throw-sites-lack-public-message, tool-scripting-undo-workflow-argument-refusals-public-message, symbol-locator-and-source-text-tool-argument-refusals-public-message, get-prompt-text-unknown-prompt-public-message, resource-argument-refusals-public-message, root-boundary-argument-refusals-public-message, dispatch-and-tool-argument-refusals-public-message, host-config-argument-guards-not-caller-errors, catalog-internal-argument-guards-not-caller-errors, parameter-object-argument-refusals-public-message, change-signature-service-refusals-public-message, format-range-service-refusals-public-message, bulk-refactoring-argument-refusals-public-message, type-extraction-argument-refusals-public-message, restructure-and-semantic-grep-argument-refusals-public-message, edit-service-argument-refusals-public-message, suppression-argument-refusals-public-message, code-action-and-flow-argument-refusals-public-message, navigation-and-locator-argument-refusals-public-message, symbol-handle-and-reference-argument-refusals-public-message, workspace-lifecycle-argument-refusals-public-message, roslyn-internal-argument-guards-not-caller-errors, preview-token-argument-refusals-public-message, build-and-analysis-argument-refusals-public-message, scripting-and-snippet-argument-refusals-public-message, file-create-and-scaffold-refusals-public-message, tool-error-handler-argument-sniffing-arms-retired, release-managed-guard-worktree-scope`

## Anchors

- `src/BannedSymbols.txt`
- `src/Directory.Build.props`
- `src/RoslynMcp.Core/Services/PublicArgumentException.cs`
- `src/RoslynMcp.Core/Services/ArgumentErrors.cs`

## Acceptance

- [ ] A full Release build of the solution reports 0 RS0030; a scratch `new ArgumentException(msg)` or `new ArgumentException(msg, name)` in any src project fails the build, then is reverted.
- [ ] tests/, analyzers/ and samples/ projects are unaffected (root BannedSymbols.txt unchanged).
- [ ] Only PublicArgumentException.cs and ArgumentErrors.cs carry a scoped RS0030 pragma.
- [ ] Writing the src-scoped BannedSymbols.txt uses the `.release-managed-edit-allowed` sentinel in the executor's own checkout (the per-checkout resolution added by `release-managed-guard-worktree-scope`), never the shared primary root, and removes it before commit.

## Evidence

- 0 ArgumentException/ArgumentOutOfRangeException construction site(s) in scope at main f7b33b85; plain ArgumentException messages are redacted by `ToolErrorHandler.BuildSafeArgumentMessage` (`src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs:570-646`) to "Parameter '<x>' is invalid", or rescued only by message/paramName sniffing arms. Survey + design: plan-deepener, backlog-remediate 20260926T234932Z.

## Context

One child of the argument-error contract redesign (operator decision 2026-09-26: correct fix over the narrow paramless-ctor ban). Estimated diff ~35000 tokens.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
