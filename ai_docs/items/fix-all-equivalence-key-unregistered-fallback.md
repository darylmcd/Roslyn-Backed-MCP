# fix-all-equivalence-key-unregistered-fallback — Refuse an unregistered FixAll equivalence key

**row:** `fix-all-equivalence-key-unregistered-fallback` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FixAllService.cs:317`
- `tests/RoslynMcp.Tests/FixAllServiceIntegrationTests.cs`

## Acceptance

- [ ] When no provider action yields an equivalence key, FixAll returns an actionable refusal or uses a documented keyless action contract; it never substitutes the provider type name as an unverified key.
- [ ] Regression covers a provider that registers actions without an equivalence key and confirms no silent zero-change or wrong-action result.

## Evidence

- `ResolveEquivalenceKeyAsync` currently returns `provider.GetType().Name` after registrations yield no key; its own comment says the value may not work. This is separate from analyzer-reference provider discovery.
