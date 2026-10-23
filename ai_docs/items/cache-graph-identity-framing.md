# cache-graph-identity-framing - Frame cache graph identities without delimiter ambiguity

**row:** `cache-graph-identity-framing` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceCacheCoordinator.cs:145`
- (new) `tests/RoslynMcp.Tests/WorkspaceCacheGraphIdentityTests.cs`

## Acceptance

- [ ] Reproduce distinct graph nodes whose valid Unix path characters collide under tab/newline concatenation; retain ordinary graph-order independence.
- [ ] Replace delimiter-only framing with an unambiguous structured identity covering every existing graph field and exact string value.
- [ ] Qualify Unicode encoding, repeated fields and empty values without changing compiler authority or accepting a cache entry solely on its key.

## Evidence

- 2026-10-22 cold review observed ComputeMsbuildGraphHash concatenating project and reference paths with tab, pipe and newline delimiters, then hashing UTF-8 bytes. Native filesystem paths can contain those delimiters on Unix; UTF-8 replacement fallback can also erase distinct unpaired UTF-16 values.
- The current loader independently validates native graph coordinates and current metadata inputs; this separate existing hash mechanism affects cache identity/hit hints. The exact collision regression remains to be added and observed.
