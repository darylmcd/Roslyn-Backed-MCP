# relocation-alias-static-imports-of-emptied-namespace — Relocation import pass mishandles alias and static imports of the emptied namespace

**row:** `relocation-alias-static-imports-of-emptied-namespace` · **pri:** `Medium` · **size:** `S` · **deps:** `namespace-relocation-consumer-scope-identity`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:642-669`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] A consumer with `using A = Old;` compiles after change_type_namespace empties Old: the alias is retargeted to the new namespace (checked against the updated compilation: Old may still exist through a child namespace such as `Old.Sub`). `using static Old;` on a namespace is invalid C# (CS7007) and is not a case.
- [ ] Red-first test (flip the existing known-gap test in NamespaceRelocationTests); project-wide `global using Old;` covered or explicitly refused. Depends on the per-scope consumer rewrite of `namespace-relocation-consumer-scope-identity` (the spelling gate currently skips a file whose only directive is the alias).

## Evidence

- The import pass matches `u.Name?.ToString()` against the namespace without checking `Alias`/`StaticKeyword`, so an alias or static directive counts as the plain import. Found in the slice-B1 review (reloc-b report, bad-code sketch 2); slice B1 (relocation-using-semantic-identity) deliberately leaves this open and relies on this row.
