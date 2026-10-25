# test-trx-definition-qualified-identity — Resolve failed TRX methods through test definitions

**row:** `test-trx-definition-qualified-identity` · **pri:** `Medium` · **size:** `S` · **deps:** —

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs:302`
- `tests/RoslynMcp.Tests/TestRunFailureEnvelopeTests.cs`

## Acceptance

- [ ] Resolve result testId through a unique UnitTest/TestMethod in the same report; preserve class-qualified method identity and parameterized display labels.
- [ ] Cover equal display labels in different classes, duplicate/missing definitions and report-local IDs; observe old-red/new-green without accepting ambiguous identity.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. ParseSingleTrxFile assigns result testName directly to FullyQualifiedName. Retained DotnetOutputParser and tests include unmerged definition lookup.
- Retained immutable source: `d333c2e96a1e23a31286cda53b920dbee5d6b7be`; preserved tag: `archive/pr1806-restore-reviewed-graph-20261025`. No build/test ran in this content audit; historical receipts are not current runtime qualification.
