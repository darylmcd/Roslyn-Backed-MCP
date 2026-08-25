# backlog-dangling-deps-closed-observability-rows — Clean 7 dangling deps references to closed observability rows

**row:** `backlog-dangling-deps-closed-observability-rows` · **pri:** `Low` · **size:** `S`

## Anchors

- `ai_docs/backlog.md:63`
- `ai_docs/backlog.md:139`

## Acceptance

- [ ] No deps cell references a non-existent row id — 6 refs to `mcp-logging-stderr-otel-migration` and 1 to `server-structured-observability-sink` (both closed in PR #1253) removed or replaced via `backlog.mjs update`.
- [ ] Rows whose only dep was a closed row show `—` or their remaining real deps.

## Evidence

- `backlog.mjs get` returns "row not found" for both ids; their items/ files were deleted in PR #1253 — see `ai_docs/audits/20260825-1440/report.md` (hygiene).

## Notes

- Whether backlog-lint should flag dangling deps is global-tooling scope — belongs to the global ~/.claude backlog, not this repo.
