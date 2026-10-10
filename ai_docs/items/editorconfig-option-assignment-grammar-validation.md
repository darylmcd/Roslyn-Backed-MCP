# editorconfig-option-assignment-grammar-validation - Validate one serialized assignment

**row:** `editorconfig-option-assignment-grammar-validation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:350-353`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:391`
- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:483`
- `src/RoslynMcp.Host.Stdio/Tools/EditorConfigTools.cs:47-49`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Validate generic key/value input as exactly one provider-valid option assignment before any write transaction. Reject injected line breaks and key syntax that changes or loses the requested key; preserve valid arbitrary options, empty values and unset semantics.
- [ ] Derive accepted assignment grammar from the installed public parser; do not impose diagnostic severity enums on generic values or add a competing config grammar.
- [ ] Prove service and production-parity MCP outcomes, no disk mutation on refusal, original encoding and undo behavior. Assess ADR and migration requirements for public error contract changes.

## Evidence

- Source re-derived at a4a826dcf589068b908234759a1103cafe39bb8b: only `string.IsNullOrWhiteSpace(key)` is validated. The writer passes `key.Trim(), value.Trim()` and constructs `var assignment = $"{key} = {value}";` without enforcing a single assignment line. Embedded newline text can serialize extra assignments or headers; malformed key syntax can serialize a different or unrecognized key. This is a source trace, not a measured raw-wire exploit.
- Distinct from null-value prewrite validation and from existing effective-section precedence/parallel-section placement rows. No new implementation is admitted by this filing.
