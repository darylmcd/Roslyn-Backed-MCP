# class-split-generated-file-no-clobber — Prevent class split from overwriting files

**row:** `class-split-generated-file-no-clobber` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ClassSplitOrchestrator.cs:79-99`
- `src/RoslynMcp.Roslyn/Services/CompositeApplyOrchestrator.cs:133-175`
- `src/RoslynMcp.Roslyn/Services/CompositePreviewStore.cs:32-41`
- `tests/RoslynMcp.Tests/ClassSplitReceivingNamespaceIntegrationTests.cs`

## Acceptance

- [ ] Reject a destination that aliases the source or already exists before minting a token. Exercise a destination created after apply-time drift validation and before the atomic commit. Preserve that file and the original source. Cover physical path identity and an unused destination control.

## Evidence

- Source review at `34a87d9c`. Class split stages the new partial as a normal writable mutation and diffs against empty text. Composite apply writes the mutations in order with replacement, including when the generated path and source are the same. Preview fingerprints detect destination drift before apply; they do not make the commit an exclusive create.
