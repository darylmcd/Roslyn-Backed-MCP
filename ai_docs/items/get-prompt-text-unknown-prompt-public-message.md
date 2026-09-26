# get-prompt-text-unknown-prompt-public-message — List available prompts on an unknown get_prompt_text name

**row:** `get-prompt-text-unknown-prompt-public-message` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:67`
- `tests/RoslynMcp.Tests/PromptShimToolsTests.cs`

## Acceptance

- [ ] get_prompt_text with an unknown promptName returns "Prompt '<name>' not found. Available prompts: …" through the tool error envelope instead of the redacted generic argument message.
- [ ] Regression test asserts the envelope message lists at least one real prompt name.

## Evidence

- get_prompt_text unknown prompt returned a generic error — mcp-surface-audit 20260924-1305 (check C1). PromptShimTools.cs:67 throws a plain `ArgumentException`, which `ToolErrorHandler.BuildSafeArgumentMessage` redacts; `PublicArgumentException` (ToolErrorHandler.cs:15) is the pass-through type.

## Context

- Split child of `invalid-operation-throw-sites-lack-public-message` (split 2026-09-26).
