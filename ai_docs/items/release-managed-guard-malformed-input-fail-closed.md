# release-managed-guard-malformed-input-fail-closed — reject malformed hook input safely

**row:** `release-managed-guard-malformed-input-fail-closed` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/guard-release-managed-files.ps1`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs`

## Acceptance

- [ ] Malformed JSON, a missing/non-object `tool_input`, and a missing/non-string `file_path` fail closed with the hook's blocking exit code instead of allowing the edit.
- [ ] Empty stdin remains an explicitly documented compatibility case, or fails closed if the hook host always supplies input.
- [ ] Diagnostics name only the malformed field/category; they never echo raw hook JSON, paths, or values.
- [ ] A process-level regression covers valid unmanaged input, valid managed input, malformed JSON, and shape-invalid payloads with exact exit behavior.

## Evidence

`eng/guard-release-managed-files.ps1` currently catches `ConvertFrom-Json` failure and exits 0, and also allows absent `tool_input.file_path`. A malformed hook frame can therefore bypass the release-managed-file guard entirely.
