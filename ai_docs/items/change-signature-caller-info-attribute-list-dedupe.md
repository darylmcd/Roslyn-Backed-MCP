# change-signature-caller-info-attribute-list-dedupe — Extract one shared caller-info attribute list

**row:** `change-signature-caller-info-attribute-list-dedupe` · **pri:** `Low` · **size:** `M` · **verified:** `96629cae1d46b5daac93e90539ff5bc624f57daa`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs:421`
- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs:28`
- `src/RoslynMcp.Roslyn/Services/ParameterObjectService.cs:1391`

## Acceptance

- [ ] One shared constant/helper owns the four caller-info attribute names (`CallerLineNumber`, `CallerFilePath`, `CallerMemberName`, `CallerArgumentExpression`), used by `ChangeSignatureAddRemovePreviewBuilder.cs:421-424` and `InterfaceMemberProjector.cs:28-31` (`_contractAttributes`, which adds further contract attributes on top).
- [ ] `ParameterObjectService.cs:1391` lists a different pair (`CallerArgumentExpression` plus `InterpolatedStringHandlerArgument`); decide whether it shares the helper or stays separate, and say why in a comment.
- [ ] Existing change-signature, interface-extraction and parameter-object tests stay green.

## Evidence

- Seen during PR 2173 review: three copies of overlapping literal attribute-name lists, drift-prone when a new caller-info attribute is added.
