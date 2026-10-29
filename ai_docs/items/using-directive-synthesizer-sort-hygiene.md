# using-directive-synthesizer-sort-hygiene — Fix IsSystemUsingDirective StartsWith("System") mis-sort and the null-forgiving OrderBy key in UsingDirectiveSynthesizer

**row:** `using-directive-synthesizer-sort-hygiene` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs` (IsSystemUsingDirective, OrderBy u.Name!)
- `tests/RoslynMcp.Tests/UsingDirectiveSynthesizerTests.cs`

## Acceptance

- [ ] `using SystemTools.X;` is not treated as a System namespace (match `System` or `System.` only).
- [ ] Sort key handles null names via GetUsingNamespace without `!`.

## Evidence

- Observed during generated-interface-global-using-order: `SystemFoo.X` sorts with System; `u.Name!` beside null-handling GetUsingNamespace.
