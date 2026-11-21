# extract-type-namespace-scoped-usings-dropped — Carry namespace-scoped usings onto the extracted file

**row:** `extract-type-namespace-scoped-usings-dropped` · **pri:** `Medium` · **size:** `S` · **verified:** `e71aace5065e4d5a7edbefb3068e02c0a2579312`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Extracting a type whose members need a `using` declared INSIDE the enclosing namespace block produces a compiling new file (red-first test; same-mechanism check for aliases and static usings).

## Evidence

- Plan review of extract-type-nested-namespace-identity (sketch 2); not probed at runtime.
