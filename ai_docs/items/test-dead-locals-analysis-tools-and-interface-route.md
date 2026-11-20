# test-dead-locals-analysis-tools-and-interface-route — Remove two dead locals in tests

**row:** `test-dead-locals-analysis-tools-and-interface-route` · **pri:** `Low` · **size:** `S` · **verified:** `02fddfa9500d30848b8f5e34367322db1011b3df`

## Anchors

- `tests/RoslynMcp.Tests/AnalysisToolsTests.cs:371`
- `tests/RoslynMcp.Tests/InterfaceSignatureReferenceRouteTests.cs:91`

## Acceptance

- [ ] `server` (AnalysisToolsTests.GoToDefinition_CaretOnWhitespace_ReportsPositionSpecificMessage) and `before` (InterfaceSignatureReferenceRouteTests.Independent_Signature_Owner_Gets_Reference_And_Compiles): decide per local whether an assertion is missing or the local is noise; add the assertion or drop the local.

## Evidence

- Surface audit 2026-11-20 (server 5.1.1), find_dead_locals: 2/2 hits verified by reading the methods.
