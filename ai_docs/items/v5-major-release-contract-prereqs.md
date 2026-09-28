# v5-major-release-contract-prereqs — ADR + product-contract coverage for pending BREAKING fragments before the 5.0.0 cut

**row:** `v5-major-release-contract-prereqs` · **pri:** `Defer` · **size:** `S`

## Anchors

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

- [x] Operator decision recorded 2026-09-28: fold `locationdto-next-major-flat-field-removal` into the 5.0.0 breaking set; ADR 0011 covers all three changes.
- [x] Alternative decided 2026-09-28: #1553/#1571 were reworked to additive 4.x changes (`checkStatus` is the "unknown" authority; unknown `workspaceId` keeps `NotFound` and adds `reason: "WorkspaceNotFound"`) so main ships 4.3.0. The contract changes wait for 5.0 as `server-info-update-available-next-major-nullable` and `workspace-not-found-next-major-category-promotion`. The Problem table describes the pre-rework state.
- [ ] At the 5.0.0 cut, ADR `docs/decisions/0011-*.md` records all three changes (old→new wire behavior and consumer migration for each) and `docs/decisions/README.md` indexes it.
- [ ] In the same release, `docs/product-contract.md` § Deprecations scheduled for 5.0 is replaced by the 5.0 contract.

## Regression shape

Docs-only; `verify-ai-docs` / link gates pass. No production code change.

## Notes

- 2026-09-28: raised to High (retro 2026-09-27 `fixes-stranded-unreleased-4-2-1-pin`). 26 Fixed fragments are unreleased (oldest added 2026-09-19) while `.claude-plugin/mcp.json` pins 4.2.1, so every consumer runs without them. The two BREAKING changes are already merged (#1553 `77c1055d`, #1571 `09728c88`), so a 4.x cut from HEAD needs Acceptance bullet 4 (additive rework) first; otherwise finish the ADRs and cut 5.0.0. Which route to take is an operator decision. Companion: `release-lag-guard`.
- 2026-09-28 (PR #1663): #1553 and #1571 now ship as additive 4.x changes. No `Changed — BREAKING` fragment is pending, and `docs/product-contract.md` already carries the 4.x contract and the 5.0 deprecation table. The Problem table, including its "this row blocks that cut" sentence, describes the pre-rework state. This row no longer blocks the 4.3.0 cut. It is parked (Defer) until the 5.0.0 cut, where it runs with `server-info-update-available-next-major-nullable` and `workspace-not-found-next-major-category-promotion`.
