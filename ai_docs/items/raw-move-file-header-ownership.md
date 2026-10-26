# raw-move-file-header-ownership — Preserve file roles when moving existing declarations

**row:** `raw-move-file-header-ownership` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (raw moved-file construction and source removal)
- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs` (relocating builder and source removal)
- `src/RoslynMcp.Roslyn/Helpers/MovedDeclarationFileWriter.cs` (new shared owner)
- `tests/RoslynMcp.Tests/MovedDeclarationFileOwnershipIntegrationTests.cs` (new native public-route controls)

## Acceptance

- [ ] Share raw moved-file construction and declaration removal between both owners. Separate physical file prelude from declaration XML documentation/body; preserve actual EOF trivia, generated classification and file policy before the receiving namespace, exactly once.
- [ ] Strip source prelude from a moved declaration only when its first token physically owns it. Remove selected documentation without attaching it to remaining declarations; retain unrelated documentation, source prelude and necessary directive balancing. Preserve caller deletion/authorization decisions and namespace-local imports.
- [ ] Observe red on merged main and green for BOTH native routes with first/second selected declarations, prelude owned by using/namespace/member, EOF and no-newline tails, source remainder/deletion, supported global/wrapped destinations and explicit/preserve/derived namespace selection.
- [ ] Assert valid-before compilation/runtime, serialized preview and applied/reloaded compilation/binding/runtime, file/declaration bytes and remaining documentation binding, actual compiler generated classification and nullable symbol states with/without explicit nullable directive, nonmutation/store/version/disk/project-options invariants.
- [ ] Keep namespace kind selection, receiving parse-options signatures, project policy and semantic member/nullable projection unchanged. Use modern supported wrapped receivers and supported older global controls; retain all 45 older-language candidate cases for the dependent namespace slice.

## Evidence

- Cold partition verdict namespace-cold-partition-verdict.md approves independent A then B: both raw builders lose or misplace physical file-role trivia; synthesized partial/partition callers have distinct decoration policies.
- Admitted move-generated-context-probe-final.log: valid global auto-generated source under Nullable Enable has generated=True/None annotations; old and retained wrappers put the header after namespace, yielding generated=False/NotAnnotated. Explicit nullable directive controls distinguish classification from annotations.
- Preserved CrossProject candidate has causal header/EOF/documentation failures and 116 affected passes, but is not complete across the second raw owner and is not inherited as qualification for this row.

## Scope

- Mandatory prerequisite exposed during original count=10 remediation; Directive1(a) widens scope explicitly without dropping or closing original objectives. Three production files share one raw-declaration ownership mechanism; no namespace-selector or unused-helper-only intermediate.
