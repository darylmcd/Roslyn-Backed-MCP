# editorconfig-arbitrary-assignment-revert-byte-assertions — Verify byte-exact EditorConfig revert

**row:** `editorconfig-arbitrary-assignment-revert-byte-assertions` · **pri:** `Low` · **size:** `S` · **verified:** `34a87d9c712c19c0af78caca46be5d50af6ab384`

## Anchors

- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs:177-200`

## Acceptance

- [ ] The revert regression fails if the pre-existing EditorConfig file has different bytes after revert, even when the set of files matches.

## Evidence

- Source review at `34a87d9c`. The existing test snapshots EditorConfig files but checks only the restored key set.
