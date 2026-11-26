# workspace-metadata-add-native-clr-lookup — Supported metadata additions fail native CLR lookup

**row:** `workspace-metadata-add-native-clr-lookup` · **pri:** `Low` · **size:** `M` · **verified:** `0c45a92be7205924441cc176a9cf6a9dda1264bf`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:1333-1346` — delegates modified solutions to MSBuildWorkspace.
- `src/RoslynMcp.Roslyn/Contracts/IWorkspaceManager.cs:160-164` — generic modified-Solution apply contract.
- (new) `tests/RoslynMcp.Tests/WorkspaceMetadataReferenceApplyTests.cs`

## Acceptance

- [ ] Reproduce a valid metadata-reference addition on a loaded Windows workspace, assert AddMetadataReference capability, and observe the existing native failure before fixing its owning cause; retain the exact host/compiler/SDK and reference properties in the regression.
- [ ] Make that supported addition complete without the native CLR lookup failure, through the owning integration or upstream correction; preserve reference aliases and document the actual supported contract. Do not add a new MCP metadata-edit feature, suppress the exception, change machine configuration, or claim an unobserved public-tool failure.
- [ ] Preserve document-change apply and undo consumers; keep the correctly unsupported ChangeDocumentInfo capability distinct.

## Evidence

| Field | Observed evidence |
|---|---|
| Reach / origin / impact / route | L2 at the supported direct component API; pre-existing; P2; I5. A valid metadata addition advertised as supported throws before completing the operation. No ordinary MCP metadata-add consumer was found. |
| Error | `System.DllNotFoundException: Unable to load DLL 'clr' or one of its dependencies: The specified module could not be found. (0x8007007E)` |
| Call path | `GlobalAssemblyCacheLocation.GetCachePath` -> `MSBuildWorkspace.ApplyMetadataReferenceAdded` -> `WorkspaceManager.TryApplyChanges` at1342, during direct fixture setup against unchanged production. |
| Capability | Frozen Roslyn5.12 assemblies report `AddMetadataReference=True`, `ChangeDocumentInfo=False`; do not confuse the latter expected rejection with this failure. |
| Raw witness | `~/.codex/scratch/roslyn-remediate-20261125/signature-preview-latency-implement/preservation-old.log` and its raw TRX show eight setup failures before preview. `metadata-capability.log` records the supported capability probe. |
| Scope limit | Existing production callers found through semantic references apply document changes in DocumentSetPersistenceService, EditService and UndoService. No direct public MCP metadata-add crash or new feature requirement is asserted. |
| Current recovery | The latency fixture uses the existing alias-test pattern: emit the owned reference DLL and csproj Reference/Aliases before workspace load. Its eight preservation cases pass; that setup correction does not repair metadata mutation. |

## Context

The generic component contract accepts a modified Solution and the underlying workspace advertises this change kind. Track the supported integration failure without widening the latency fix. The native library's owning cause is not established; investigate before choosing an adapter or dependency change.

Deduplication: workspace-native-reference-global-context-mismatch concerns loader evaluation globals; undo-reference-keys-ignore-aliases-and-embed-interop concerns restoration comparison. Neither tracks native GAC lookup during a metadata addition.

Root triage uses the component envelope; absence of a current MCP metadata-add consumer limits impact and priority. It does not establish that this otherwise supported direct API input is malformed. Do not infer a product-wide runtime regression.
