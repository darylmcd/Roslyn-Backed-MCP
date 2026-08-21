# type-extraction-same-file-consumer-rewrite — Preserve same-file consumers

**row:** `type-extraction-same-file-consumer-rewrite` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs`
- `tests/RoslynMcp.Tests/TypeExtractionTests.cs`

## Acceptance

- [ ] Find retained source-type members that reference an extracted member in the same document.
- [ ] Rewrite eligible references through the injected composition field, or refuse before storing a preview when the binding cannot be preserved.
- [ ] Keep overload, static, and method-group binding semantic rather than name-based.
- [ ] Add one compiling-preview regression where a retained member calls an extracted method.

## Evidence

- A current-session Unicode-name fixture produced CS0103 after extraction because `InternalUser()` retained `Compute(21)` while `Compute` moved to the generated type; external-consumer discovery deliberately ignores the source file and no same-file rewrite follows.
