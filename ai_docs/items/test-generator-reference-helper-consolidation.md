# test-generator-reference-helper-consolidation — Share one test helper for source-generator references

**row:** `test-generator-reference-helper-consolidation` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/InterfaceExtractionTestContext.cs`:120-160
- `tests/RoslynMcp.Tests/DiagnosticQueryServiceRegressionTests.cs`:217
- `tests/RoslynMcp.Tests/MovedTypeDestinationCollisionIntegrationTests.cs`:307-317

## Acceptance

- [ ] One helper builds a generator analyzer reference that passes the workspace checksum find-references runs; the three classes use it; their tests stay green.

## Evidence

- iface-extraction report (PR 2145), sketch 5: generator scaffolding duplicates the ad-hoc generator references; non-file AnalyzerReference subclasses fail the workspace checksum. PR 2148 moved the interface copy into InterfaceExtractionTestContext; the other two copies remain at the lines above (verified at 12bc2c8b).
