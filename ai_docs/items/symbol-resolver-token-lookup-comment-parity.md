# symbol-resolver-token-lookup-comment-parity — align token lookup comment with implementation

**row:** `symbol-resolver-token-lookup-comment-parity` · **pri:** `Low` · **size:** `S` · **deps:** `navigation-and-locator-argument-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:285-287` — comment claims a second FindToken lookup with findInsideTrivia; actual lookup uses FindToken(position).
- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:316-321` — preceding-token fallback uses FindToken(position - 1) only in lenient mode.

## Acceptance

- [ ] Describe actual exact-position lookup and lenient preceding-token fallback without claiming findInsideTrivia is used.
- [ ] Preserve runtime behavior and existing strict/lenient semantics.
- [ ] Search other comments describing this same lookup mechanism and correct any matching stale claim.

## Evidence

- Verified 2026-10-09 during navigation remediation: findInsideTrivia occurs only in the comment in this file; actual calls use position and position - 1.
- Immutable base e52c124196c350a334e30b5b925274f3e9974b46 carries the same comment at lines 294-295; the navigation PR does not introduce this unrelated documentation debt.
- One production file; no behavior change or new tests required.
