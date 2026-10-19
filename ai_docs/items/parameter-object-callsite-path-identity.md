# parameter-object-callsite-path-identity - Preserve case-distinct caller summaries

**row:** `parameter-object-callsite-path-identity` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs`
- `tests/RoslynMcp.Tests/ParameterObjectPreviewTests.cs`

## Acceptance

- [ ] Accumulate parameter-object caller summaries with filesystem-aware physical path identity.
- [ ] Inventory every same-mechanism grouping in the service; preserve Windows case-insensitive aggregation.
- [ ] Observe a native Linux regression fail before repair: two case-distinct caller documents retain separate CallsiteUpdateDto entries matching actual preview/apply edits.

## Evidence

- Source-only inspection at main000a1ce0: PreviewParameterObjectAsync creates perFileCallsites with StringComparer.OrdinalIgnoreCase before rewriting calls; the projection can collapse separate Linux documents even while document edits remain distinct.
- Found while reviewing shared parameter-object eligibility for correctness15. No runtime reproduction claimed. This path-summary mechanism is separate from target/group eligibility and from the existing change-signature-callsite-update-path-identity row's defining builder.
