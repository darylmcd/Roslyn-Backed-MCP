# editorconfig-section-source-path-matching — Match editorconfig sections against the source path

**row:** `editorconfig-section-source-path-matching` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/EditorConfigService.cs:161`
- `tests/RoslynMcp.Tests/EditorConfigServiceTests.cs`

## Acceptance

- [ ] Disk option overlay applies each section only when its glob matches the requested source file relative to that .editorconfig file.
- [ ] Regression covers a path-qualified C# glob that should match one source directory but not another, while ordinary [*.cs] and [*] sections still apply.

## Evidence

- `SectionMatchesCSharp` accepts any section ending in `.cs` for every C# file, regardless of its path. `ParseEditorconfigCsKeys` uses that predicate for GetOptionsAsync overlay, so a path-qualified section can leak options into unrelated documents. This differs from the set-option duplicate-section writer issue.
