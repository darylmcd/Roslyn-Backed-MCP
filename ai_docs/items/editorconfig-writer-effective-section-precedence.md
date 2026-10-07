# editorconfig-writer-effective-section-precedence — Preserve effective values across matching editorconfig sections

**row:** `editorconfig-writer-effective-section-precedence` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:510`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Preserve the documented broad C# writer policy while ensuring later matching sections cannot leave the requested key's effective value unchanged after a reported successful write.
- [ ] Resolve every instance of first-match update under that policy without introducing a competing glob grammar or changing unrelated document overrides without an explicit policy decision.
- [ ] Real disk/write/effective-option regressions cover repeated keys, overlapping sections, later narrow overrides, arbitrary keys and diagnostic severities; preserve encoding and transaction/undo guarantees.

## Evidence

- At base 6cf842a8b7ea8ac56d7706e9c91c451d1852d9b4, UpsertKeyAcrossCSharpSections replaces the first matching key and returns (EditorConfigService.cs:510-525). Later matching assignments retain higher effective precedence. Reader matching repair is separately tracked by editorconfig-section-source-path-matching; section reuse without duplicate keys is tracked by set-editorconfig-option-creates-parallel-section.
