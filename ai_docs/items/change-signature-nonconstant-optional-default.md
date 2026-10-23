# change-signature-nonconstant-optional-default

**row:** `change-signature-nonconstant-optional-default` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`

## Acceptance

- [ ] Refuse a nonconstant optional parameter default before issuing an uncompilable preview.
- [ ] Preserve supported constant defaults and compiler-valid callsite migration.
- [ ] Distinguish declaration defaults from any separately supported required-callsite argument expression; do not silently make required parameters optional.

## Evidence

- This session: change_signature_preview add System.TimeProvider with defaultValue System.TimeProvider.System returned a declaration containing a nonconstant optional initializer. The preview was inspected and never applied.
- Workaround after filing: use the semantic caller inventory and explicitly migrate required declaration and callers.
