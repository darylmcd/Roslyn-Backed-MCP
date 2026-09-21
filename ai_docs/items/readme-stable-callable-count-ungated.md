# readme-stable-callable-count-ungated — Gate the stable-only callable count at `README.md:186`

**row:** `readme-stable-callable-count-ungated` · **pri:** `Medium` · **size:** `S`

## Anchors

- `README.md:186`
- `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs:33`

## Acceptance

- [ ] `README.md:186`'s stable-only callable count ("currently 94 callable tools") is asserted against the live stable-tier callable selection, or the claim is removed/reshaped so no ungated number remains.
- [ ] A regression test fails when the documented stable-only count drifts from the catalog.

## Evidence

`ReadmeSurfaceCountTests.CountPattern` (`tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs:33`) requires the bolded `**N tools**` form. `README.md:186` carries the count as unbolded prose inside the `ROSLYNMCP_TOOL_TIERS` table row ("currently 94 callable tools"), so it matches nothing. `grep -rn "callable" tests/RoslynMcp.Tests/*.cs` returns zero hits — no other test asserts it.

The claim that it IS gated is propagated into ~34 backlog item files (e.g. `ai_docs/items/promotion-tier-symbols.md`, "gated by `tests/RoslynMcp.Tests/ReadmeSurfaceCountTests.cs`") and, until this audit, into `ai_docs/prompts/backlog-sweep-addenda.md`. Every tier-promotion row therefore believes a silent drift will be caught. It will not.
