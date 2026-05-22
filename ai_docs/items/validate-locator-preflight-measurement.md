# Locator Preflight Measurement

<!-- purpose: Decide whether a separate locator preflight tool is still justified after schema hints. -->
<!-- scope: in-repo -->

## Question

`validate-locator-preflight-measurement` asked for measurement before any
implementation: after PR #483 added schema hints to locator-consuming tools, are
agents still hitting enough `InvalidArgument` locator-shape failures to justify a
separate preflight tool?

## Sample Source

Primary sample: `ai_docs/reports/20260521T043918Z_roslyn-backed-mcp_roslyn-mcp-multisession-retro.md`.

This report is post-PR #483 and covers 40 sampled sessions from the prior
30-day window, with the sample skewed toward surface-test and backlog-sweep
work. The report counted 73 Roslyn MCP-related tool errors and 26
`InvalidArgument` errors.

## Measurement

Numerator: 0 current non-intentional `InvalidArgument` locator-shape failures.

Denominator: 26 total `InvalidArgument` Roslyn MCP errors in the sampled window.

Supporting checks:

- The report classifies the 26 `InvalidArgument` errors as mostly intentional
  surface-test bad-input probes such as impossible line numbers, bogus IDs, and
  missing required keys.
- The report's non-intentional locator-adjacent issue is not a locator-shape
  `InvalidArgument`. It is `NotFound` from slightly wrong `metadataName` values
  on `find_consumers`, `find_references`, and `find_implementations`, and that
  was already routed to closest-match suggestion work.
- Historical locator vocabulary risk around `character` vs `column` was already
  measured in `ai_docs/items/parameter-naming-canonicalization-design.md`; the
  live enumeration there found locator-consuming tools already using `column`.

## Decision

Mark the standalone locator preflight idea obsolete.

The current measured rate is 0 / 26 for the error class this row was meant to
address, so it clears the row's "drop >=80%" retirement threshold. A new
preflight tool would add surface area without current evidence that it would
remove real agent friction.

Do not add a follow-on implementation row. Future locator work should come from
fresh evidence of actual bad locator input failures, not from the retired PR
#483 follow-up.

## Validation Notes

No production code changed for this row. The validation artifact is this
measurement note plus backlog cleanup.
