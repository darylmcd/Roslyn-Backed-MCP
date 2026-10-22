# compile-check-file-scope-path-identity - Preserve physical file scope on Linux

**row:** `compile-check-file-scope-path-identity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`
- `tests/RoslynMcp.Tests/CompileCheckServiceTests.cs`

## Acceptance

- [ ] Use filesystem-aware path identity for file-filter normalization, owner discovery and diagnostic filtering.
- [ ] Inventory all same-mechanism path comparisons in the service; preserve Windows case-insensitive behavior and path normalization.
- [ ] Observe native Linux old-red/new-green proof using case-distinct source documents and project paths; a requested file must not admit a sibling's diagnostics or lose a distinct requested owner.

## Evidence

- Source-only inspection at main000a1ce0: NormalizeFileFilters and FindOwningProjects use OrdinalIgnoreCase sets for physical paths, including normalizedFileFilters and unresolvedFiles. Those sets collapse case-distinct Linux paths.
- Observed while completing public recovery guidance, whose string-only changes do not modify file-scope identity. No runtime reproduction claimed; independent from project-name matching, which intentionally remains case-insensitive.
