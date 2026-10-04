# preview-store-explicit-internal-state — preview-store-explicit-internal-state

**row:** `preview-store-explicit-internal-state` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/PreviewStore.cs:68` legacy overload defaults diffTruncated=false; comment retains it to avoid updating callers/tests.
- `src/RoslynMcp.Roslyn/Contracts/IPreviewStore.cs:23` four-argument contract and fallback overloads.
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs:524` uses the legacy call.
- `src/RoslynMcp.Roslyn/Services/BatchTestScaffolder.cs:236` uses the legacy call.
- `tests/RoslynMcp.Tests/PreviewStoreTests.cs` and relevant signature/scaffolding tests.

## Acceptance

- Re-derive all Store overloads and callers semantically. Require every internal producer to supply correct truncation state and route provenance rather than inheriting false/Unspecified defaults.
- Migrate tests and helpers with production callers; no convenience compatibility overload solely to avoid that migration.
- Prove truncated previews cannot be applied without the documented explicit override, and incompatible apply routes reject tokens; retain valid untruncated behavior.
- Assess actual external consumers before removing public contracts. Follow the public repo ADR, migration-note and deprecation policy for any externally breaking change.

## Evidence

- Current source explicitly labels the four-argument overload legacy and forwards diffTruncated:false.
- Live text probe resolves current ChangeSignatureService and BatchTestScaffolder calls without explicit preview state. Exact transport/truncation consequences require implementation-time re-derivation; no exploitation claim is made.

