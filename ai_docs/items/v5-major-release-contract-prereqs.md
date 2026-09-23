# v5-major-release-contract-prereqs — ADR + product-contract coverage for pending BREAKING fragments before the 5.0.0 cut

**row:** `v5-major-release-contract-prereqs` · **pri:** `Medium` · **size:** `S` · **deps:** `—`

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
