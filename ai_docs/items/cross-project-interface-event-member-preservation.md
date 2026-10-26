# cross-project-interface-event-member-preservation — Preserve extracted public events

**row:** `cross-project-interface-event-member-preservation` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs:602` (`CreateInterfaceMember`)
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` (`CollectReferencedNamespaces`)
- `tests/RoslynMcp.Tests/CrossProjectRefactoringIntegrationTests.cs`

## Acceptance

- [ ] Emit public instance event contracts from the selected source type with the correct delegate identity and supported event syntax.
- [ ] Preserve ordinary members and exclude nonpublic/static events; exercise direct extraction, dependency inversion and extract-and-wire preview plus apply/reload.
- [ ] Prove generated interface event symbols and public subscription compilation through red-first semantic tests; coordinate delegate dependency ownership with cross-project-interface-signature-dependencies.

## Evidence

- Main 0e0f90661035a38e570627b1508dac2ea2496708 collects event namespaces but CreateInterfaceMember handles only ordinary methods/properties, silently omitting events; current-session semantic inspection confirms the missing branch. No runtime qualification claimed.

- Current delivery order, superseding fixture diagnosis and preserved-candidate receipt limits: `ai_docs/reports/2026-10-26-interface-split-delivery.md`. Historical pending/hold statements above describe the original combined candidate; use that report for current state.
