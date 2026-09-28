# get-prompt-text-unknown-prompt-public-message — List available prompts on an unknown get_prompt_text name

**row:** `get-prompt-text-unknown-prompt-public-message` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move, argument-errors-redacted-factory`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs`
- `tests/RoslynMcp.Tests/PromptShimToolsTests.cs`

## Acceptance

- [ ] get_prompt_text with an unknown promptName lists available prompts through the envelope.
- [ ] Missing required prompt parameters are named; no raw parametersJson is echoed.
- [ ] PromptParameterBindingException and the ToolErrorHandler.cs:585 arm are deleted.

## Evidence

- get_prompt_text unknown prompt returned a generic error — mcp-surface-audit 20260924-1305 (check C1). PromptShimTools.cs:67 throws a plain `ArgumentException`, which `ToolErrorHandler.BuildSafeArgumentMessage` redacts; `PublicArgumentException` (ToolErrorHandler.cs:15) is the pass-through type.

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26).
2026-09-26: re-scoped as a child of the argument-error contract redesign.

Family design (invariant, P/R/I classification, ban scope, exceptionType normalization): see `items/public-argument-exception-core-move.md` § Family design.
