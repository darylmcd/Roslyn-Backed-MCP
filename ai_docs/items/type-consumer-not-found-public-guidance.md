# type-consumer-not-found-public-guidance — Preserve safe missing-type correction

**row:** `type-consumer-not-found-public-guidance` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:51-55`
- `tests/RoslynMcp.Tests/TypeConsumersServiceTests.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Return a path-free missing-type correction through PublicInvalidOperationException, without interpolating caller typeName; retain fully qualified/short-name and generic-arity guidance.
- [ ] Keep released InvalidOperation category and InvalidOperationException envelope identity; do not change this to a not-found category in a minor release.
- [ ] Observe old-red/new-green actual tool-envelope coverage with hostile typeName and valid type-consumer controls; retire only the replaced ratchet identity.

## Evidence

- Source-only 2026-10-15 at maina591ebfc: FindTypeConsumersAsync constructs a plain InvalidOperationException when ResolveTypeByNameAsync returns null. Its useful metadata-name/generic-arity correction is discarded by the host's defensive plain-exception classifier.
- The raw exception interpolates caller typeName; converting its type without rewriting the message would publish caller free text. No wire reproduction or leak claim is made for this filing.
- Different category/target-resolution mechanism from the selected argument-guard row; existing argument corrections and successful consumer analysis remain in the current batch.
