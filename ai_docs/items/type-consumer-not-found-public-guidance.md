# type-consumer-not-found-public-guidance — Preserve safe missing-type correction

**row:** `type-consumer-not-found-public-guidance` · **pri:** `Medium` · **size:** `S` · **deps:** `release-policy-correction-rule-adr`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeConsumersService.cs:51-55`
- `tests/RoslynMcp.Tests/TypeConsumersServiceTests.cs`
- `tests/RoslynMcp.Tests/TestData/invalid-operation-construction-baseline.json`

## Acceptance

- [ ] Derive current tool-envelope identity and metadata-name guidance from FindTypeConsumersAsync and the shared NotFound classifier.
- [ ] Qualify stable navigation and every shared producer with not-found-metadata-only-message-classification before selecting.
- [ ] Raw-wire coverage exercises hostile caller type text, actual metadata-only lookup, and valid consumer analysis. Preserve the released category and existing safe navigation guidance in 5.x.

## Evidence

- Source-only 2026-10-15 at maina591ebfc: FindTypeConsumersAsync constructs a plain InvalidOperationException when ResolveTypeByNameAsync returns null. Its useful metadata-name/generic-arity correction is discarded by the host's defensive plain-exception classifier.
- The raw exception interpolates caller typeName; converting its type without rewriting the message would publish caller free text. No wire reproduction or leak claim is made for this filing.
- Different category/target-resolution mechanism from the selected argument-guard row; existing argument corrections and successful consumer analysis remain in the current batch.

2026-10-15 current-main re-vet at000a1ce0: TypeConsumersService.FindTypeConsumersAsync currently throws KeyNotFoundException for an unresolved type, not the InvalidOperationException described above. Re-derive actual published tool category before implementation; do not blindly change released category to satisfy stale acceptance. This independent row remains open and was excluded from the correctness15 batch.

## Notes

5.x screen at a12412bb: the stale InvalidOperation acceptance conflicts with current NotFound/KeyNotFoundException on the experimental find_type_consumers tool. The installed 5.0.1 server reproduced a misleading metadata-only message when an unknown name contained that phrase. Leave unselected while assessing the shared classifier and stable navigation companions. See not-found-metadata-only-message-classification.

TypeConsumersService.cs:52 now throws KeyNotFoundException. ToolErrorHandler.cs:118,564 shares caller-influenced classification with stable navigation (SymbolNavigationService.cs:93). Qualify companion not-found-metadata-only-message-classification across stable callers before selecting. If the complete fix changes the stable response, reopen at the next operator-approved major with ADR, migration note and both raw protocol eras.

Operator decision 2026-11-20: corrections are minor. Unparked. Qualify this row against the amended docs/release-policy.md (row release-policy-correction-rule-adr) with before/after wire evidence: same response shape and error categories with an incorrect result made correct ships in 5.x as Fixed; a changed shape, category or published message text re-parks the row as major-only with the evidence quoted.
