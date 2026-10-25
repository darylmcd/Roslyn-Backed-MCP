# code-action-preview-invalid-operation-classification - Classify preview caller failures and provider invariants

**row:** `code-action-preview-invalid-operation-classification` · **pri:** `Medium` · **size:** `S` · **deps:** `code-action-index-public-refusal`

## Anchors

- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs:151` - unresolved workspace document throws plain InvalidOperationException.
- `src/RoslynMcp.Roslyn/Services/CodeActionService.cs:175` - selected provider action without ApplyChangesOperation throws plain InvalidOperationException.
- `tests/RoslynMcp.Tests/CodeActionArgumentRefusalWireTests.cs` - deterministic real-service provider and both protocol-era harness examples after the index-guidance PR lands.

## Acceptance

- [ ] Re-derive both failure owners. Publish a safe missing-document caller correction through PublicInvalidOperationException; classify unsupported provider action semantics as an explicit invariant/internal failure or a supported actionable state, rather than blindly publishing provider detail.
- [ ] Preserve released category and BCL exception identity, preview/token state, successful actions, and complete-envelope redaction of paths, source, action titles and arbitrary provider text.
- [ ] Observe causal old-red/new-green real-service deterministic-provider wire coverage in both protocol eras, including the no-ApplyChangesOperation branch and valid preview control.
- [ ] Probe all same-mechanism plain InvalidOperationException constructors in CodeActionService; use existing Core factories and host boundary without message sniffing or optional test hooks.

## Evidence

- Independent cold implementation review of code-action-index-public-refusal at base `7688a4e51ed678117cc3d0c4ee01cf2668125df9` identified both pre-existing plain-exception boundaries. Source inspection confirms the generic host fallback discards the missing-document correction; no runtime claim is made for this follow-up.
- The index correction is a separate ArgumentException publication mechanism. The provider branch must not be indiscriminately converted into a public caller error.
