# ADR 0028: Refuse editorconfig input that is not one provider-valid assignment

Status: Accepted 2026-10-20 for the next major release.

## Context

`set_editorconfig_option` and `set_diagnostic_severity` serialized `key = value` into `.editorconfig` after checking only that the key was not blank. Embedded line breaks wrote extra assignments or section headers; key syntax such as `a=b`, `a:b`, `a b`, `[*.cs]` or a leading `#` wrote a different key or a line the provider ignores; a value ending in `#`/`;` text was silently truncated by the provider. `add_pragma_suppression` spliced the same unvalidated diagnostic id into a `#pragma` source line.

## Decision

Before any file, undo or change-tracker work, the writer proves the trimmed pair parses as exactly one assignment using the installed public Roslyn parser (`AnalyzerConfig.Parse` consumed through `AnalyzerConfigSet`): one source line, one resulting option, no parser diagnostic, key and value unchanged by the round trip (providers lowercase keys and some reserved values; comparison is case-insensitive). No second grammar and no value enums are added. Arbitrary options, empty values and `unset` remain accepted.

Refusals use `PublicArgumentException` with fixed text naming the parameter and rule; caller text is never echoed.

| Input | Previous behavior | Decision |
|---|---|---|
| Key or value containing CR, LF, NEL, LS or PS | Extra lines written | `InvalidArgument` / `ArgumentException`, parameter `key` or `value`, no write |
| Key containing `=`, `:`, whitespace, `[`/`]`, leading `#`/`;`, `/` or other characters the provider does not accept | A different or inert key written | Same refusal, parameter `key` |
| Value with inline `#` or `;` comment text | Written, then truncated by the provider | Same refusal, parameter `value` |
| `diagnosticId` with whitespace, control characters or separators (`set_diagnostic_severity`, `add_pragma_suppression`, `verify_pragma_suppresses`, `pragma_scope_widen`) | Injected editorconfig or `#pragma` lines | Refusal, parameter `diagnosticId` |
| `add_pragma_suppression` `diagnosticId` holding a comma-separated list (`CA1000,CA1001`, `CA1000, CA1001`) | Wrote a multi-id `#pragma`, but verify, widen and the idempotency guard never handled lists | Refused, parameter `diagnosticId`; one diagnostic id per call |
| Leading or trailing line break on `key`, `value` or `diagnosticId` | Silently trimmed | Refused on the raw input: a surrounding break is an injected line, not whitespace |

## Compatibility and migration

Previously accepted malformed input is now refused, an observable stable-tool change shipped in a major release under the release policy; no request field, tool name or catalog count changes. Clients must send single-line, provider-valid keys and values and match refusals by category and parameter, not prose. To clear an option send an empty value or `unset`.

`EditorConfigServiceTests`, `SuppressionServiceTests` and `EditorConfigArgumentRefusalWireTests` (protocols `2025-11-25` and `2026-07-28`) prove refusal envelopes, absence of echoed input, and unchanged file bytes, undo target and change history; valid arbitrary assignments are written once and revert byte-exactly.
