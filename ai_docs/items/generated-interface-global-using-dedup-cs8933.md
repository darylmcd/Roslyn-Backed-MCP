# generated-interface-global-using-dedup-cs8933 — Dedup required namespaces against global plain usings in generated interface files (hidden CS8933)

**row:** `generated-interface-global-using-dedup-cs8933` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/UsingDirectiveSynthesizer.cs`
- `tests/RoslynMcp.Tests/UsingDirectiveSynthesizerTests.cs`

## Acceptance

- [ ] A source `global using Ns;` enters the already-added set so a required `Ns` is not also emitted as plain `using Ns;`.
- [ ] Red-first pure helper test.

## Evidence

- generated-interface-global-using-order cold review: dedup deliberately left out of that PR as beyond its acceptance.
