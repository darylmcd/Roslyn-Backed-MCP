# docs-observability-sink-env-name-corruption — Fix corrupted env-var name n=stderr in three published docs

**row:** `docs-observability-sink-env-name-corruption` · **pri:** `Medium` · **size:** `S`

## Anchors

- `docs/upgrade-matrix.md`
- `docs/decisions/0003-sdk-2x-wire-compatibility.md`
- `docs/stdio-client-integration.md`

## Acceptance

- [ ] All three docs name `ROSLYNMCP_OBSERVABILITY_SINK=stderr`; `rg 'n=stderr' docs/` returns nothing.

## Evidence

- Three consumer-facing docs instruct operators to "set `n=stderr`" — a corrupted replacement of `ROSLYNMCP_OBSERVABILITY_SINK` (`ai_docs/runtime.md:77` retains the correct name). A consumer following them sets a nonsense env var — see `ai_docs/audits/20260825-1440/report.md` (A10).
