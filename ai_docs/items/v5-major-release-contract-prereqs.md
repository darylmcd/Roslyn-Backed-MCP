# v5-major-release-contract-prereqs — ADR + product-contract coverage for pending BREAKING fragments before the 5.0.0 cut

**row:** `v5-major-release-contract-prereqs` · **pri:** `High` · **size:** `S`

## Anchors

- `changelog.d/server-info-update-unknown-not-false.md`
- `changelog.d/workspace-id-unknown-error-category.md`
- `docs/decisions/README.md`
- `docs/product-contract.md`
- `docs/release-policy.md`

## Problem

| Fragment | Change | Policy gap |
|---|---|---|
| `server-info-update-unknown-not-false` (#1553) | stable `server_info.update.updateAvailable` `bool` → `bool?` (null = unknown) | Schema type widening on a stable tool with a FixedShape output schema; no ADR, not in product-contract |
| `workspace-id-unknown-error-category` (#1571) | unknown `workspaceId` → `category=WorkspaceNotFound` (was `NotFound`) | Category-value change; no ADR, not in product-contract |

`docs/release-policy.md` + `docs/decisions/README.md` require an ADR + migration note for every breaking change. Both `Changed — BREAKING` fragments force the next bump to `major` (`/bump` + `-RequireConsumedFragments`). Release cut deferred 2026-09-23 until backlog drain; this row blocks that cut.

## Acceptance

- [ ] ADR `docs/decisions/0011-*.md` records both changes, old→new wire behavior, and consumer migration.
- [ ] `docs/product-contract.md` documents tri-state `updateAvailable` and the `WorkspaceNotFound` category.
- [ ] Operator decision recorded: fold `locationdto-next-major-flat-field-removal` into 5.0.0 or leave it deferred.
- [ ] Alternative considered and decided: rework either change to additive (e.g. `NotFound` + discriminator field) to stay on 4.x.

## Regression shape

Docs-only; `verify-ai-docs` / link gates pass. No production code change.

## Notes

- 2026-09-28: raised to High (retro 2026-09-27 `fixes-stranded-unreleased-4-2-1-pin`). 26 Fixed fragments are unreleased (oldest added 2026-09-19) while `.claude-plugin/mcp.json` pins 4.2.1, so every consumer runs without them. The two BREAKING changes are already merged (#1553 `77c1055d`, #1571 `09728c88`), so a 4.x cut from HEAD needs Acceptance bullet 4 (additive rework) first; otherwise finish the ADRs and cut 5.0.0. Which route to take is an operator decision. Companion: `release-lag-guard`.
