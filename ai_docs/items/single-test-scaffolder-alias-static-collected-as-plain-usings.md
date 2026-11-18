# single-test-scaffolder-alias-static-collected-as-plain-usings — SingleTestScaffolder emits alias/static sibling imports as plain usings

**row:** `single-test-scaffolder-alias-static-collected-as-plain-usings` · **pri:** `Low` · **size:** `S` · **verified:** `bb6be1177b4e60b73706031338223f9018889772`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SingleTestScaffolder.cs:1129-1132`

## Acceptance

- [ ] A sibling test file with `using Alias = X.Y;` or `using static X.Z;` does not produce a plain `using` of the aliased target in the scaffold; test covers both.

## Evidence

- reloc-b report sketch 5: `root.Usings.Select(u => u.Name?.ToString())` has no Alias/StaticKeyword filter.
