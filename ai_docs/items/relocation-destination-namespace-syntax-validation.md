# relocation-destination-namespace-syntax-validation — Validate experimental namespace relocation destinations

**row:** `relocation-destination-namespace-syntax-validation` · **pri:** `Medium` · **size:** `M` · **verified:** `9e7a94395a5cbc09a79b0d4c6b8387ce65809d39`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs:81`
- `src/RoslynMcp.Roslyn/Helpers/ReceivingNamespaceFactory.cs:14`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] Through change_type_namespace_preview, prove malformed namespace names cannot mint a token for either in-place or new-file relocation. Verify escaped segments reparse and compile consistently. Probe the shared destination factory's caller contracts before changing it.

## Evidence

- At 9e7a9439 the public check tests whitespace, and the same-file routes pass caller strings to ParseName without verifying diagnostics. The receiving factory escapes segment tokens on the new-file route.
