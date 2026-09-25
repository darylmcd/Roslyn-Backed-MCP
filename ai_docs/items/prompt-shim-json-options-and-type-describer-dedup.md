# prompt-shim-json-options-and-type-describer-dedup — Align get_prompt_text argument binding with prompts/get

**row:** `prompt-shim-json-options-and-type-describer-dedup` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/PromptShimTools.cs:230`
- `src/RoslynMcp.Host.Stdio/Middleware/PromptBindingStageAdapter.cs:114`

## Acceptance

- [ ] `get_prompt_text` deserializes prompt arguments with the same options as the SDK prompt binder (`McpJsonUtilities.DefaultOptions`), so `"19"` binds to an `int` parameter as it does for `prompts/get`.
- [ ] One shared helper describes the expected JSON type; `PromptShimTools.GetExpectedJsonType` and `PromptBindingStageAdapter.GetExpectedJsonType` no longer duplicate the Nullable/enum/primitive/IEnumerable ladder.

## Evidence

- HEAD `PromptShimTools.cs:230` `return JsonSerializer.Deserialize(element.GetRawText(), p.ParameterType);` (strict default options); `PromptShimTools.cs:242` and `PromptBindingStageAdapter.cs:114` both define `private static string GetExpectedJsonType(Type parameterType)` with the same ladder (medium duplication finding, cold review of PR #1614).

## Context

Spin-off from `prompt-argument-binding-spec-strings-and-names` (PR #1614), which fixed the `prompts/get` path only.
