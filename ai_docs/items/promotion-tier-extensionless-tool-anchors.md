# promotion-tier-extensionless-tool-anchors — Add `.cs` to 32 extension-less `Tools/<Name>` anchors across 13 `promotion-tier-*` items

**row:** `promotion-tier-extensionless-tool-anchors` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/items/promotion-tier-symbols.md:7`

## Acceptance

- [ ] All 32 extension-less `src/RoslynMcp.Host.Stdio/Tools/<Name>` anchors across the 13 `ai_docs/items/promotion-tier-*.md` files carry their `.cs` extension.
- [ ] Future `promotion-tier-*` splits emit extension-bearing anchors (the lint-side guard is global tooling, tracked in `~/.claude/ai_docs/backlog.md`).

## Evidence

`grep -hoE 'src/RoslynMcp\.Host\.Stdio/Tools/[A-Za-z]+`$' ai_docs/items/promotion-tier-*.md` returns 32 anchors with no extension, e.g. `src/RoslynMcp.Host.Stdio/Tools/SymbolTools` in `promotion-tier-symbols.md`. All 32 resolve once `.cs` is appended, and `backlog.mjs audit` currently counts them, so nothing is broken yet. But a literal-path consumer (plan-deepener anchor verification, `git log -- <path>`, the addenda's companion `*Tools.cs` trigger glob) will not match them.
