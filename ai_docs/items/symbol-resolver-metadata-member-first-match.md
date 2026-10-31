# symbol-resolver-metadata-member-first-match — Refuse ambiguous metadata member lookup

**row:** `symbol-resolver-metadata-member-first-match` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/SymbolResolver.cs:429`
- `tests/RoslynMcp.Tests/MetadataMemberSymbolHandleTests.cs` (extend)

## Acceptance

- [ ] A metadata-name lookup with multiple member overloads returns a disambiguation result or a typed ambiguity, never the first overload by enumeration order.
- [ ] Cover distinct overloads through the public caller while preserving exact candidate-handle resolution and existing NotFound categories.

## Evidence

- Metadata-handle implementation review on 2026-10-31 found `GetMembers(memberName).FirstOrDefault()` in the pre-existing metadata-name fallback. The exact-handle path avoids this fallback; the broad name path remains independently ambiguous.
