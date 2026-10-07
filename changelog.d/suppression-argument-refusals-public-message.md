---
category: Changed — BREAKING
---
- **Changed — BREAKING:** `set_diagnostic_severity` now rejects null, blank and unsupported severity values as named `InvalidArgument` refusals before writes. Clients must use `error`, `warning`, `suggestion`, `silent`, or `none`; surrounding whitespace and case-insensitive acceptance remain supported, with trimmed casing preserved. Suppression and pragma refusals publish safe corrective parameter text and retain established BCL exception identities where unchanged. See [ADR 0019](../docs/decisions/0019-suppression-public-argument-diagnostics.md) for migration details.
