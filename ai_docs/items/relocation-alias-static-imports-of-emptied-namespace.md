# relocation-alias-static-imports-of-emptied-namespace — Relocation import pass mishandles alias and static imports of the emptied namespace

**row:** `relocation-alias-static-imports-of-emptied-namespace` · **pri:** `Medium` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:642-669`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] A consumer with `using A = Old;` or `using static Old;` compiles after change_type_namespace empties Old; alias/static directives are neither matched as the plain old/new import nor left dangling.
- [ ] Red-first test per directive kind; project-wide `global using Old;` covered or explicitly refused.

## Evidence

- The import pass matches `u.Name?.ToString()` against the namespace without checking `Alias`/`StaticKeyword`, so an alias or static directive counts as the plain import. Found in the slice-B1 review (reloc-b report, bad-code sketch 2); slice B1 (relocation-using-semantic-identity) deliberately leaves this open and relies on this row.
