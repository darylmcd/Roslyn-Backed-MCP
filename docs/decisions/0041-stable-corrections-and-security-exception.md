# ADR 0041 — Stable corrections and the security removal exception

- **Status:** Accepted (2026-11-20)
- **Deciders:** repository maintainers
- **Scope:** `docs/release-policy.md` compatibility rules for stable tools
- **Supersedes:** nothing
- **Refines:** ADR 0003, ADR 0011
- **Amends:** ADR 0003's "Breaking" classification of leak and correction fixes (rows at 0003:45-55 unchanged; Rule 1 and Rule 2 cases ship in a minor under this ADR)
- **Superseded by:** nothing

## Context

`docs/release-policy.md` lines 24-26 forced every stable-wire correction and every sensitive-detail removal into a major release. No v6.0.0 is scheduled, so those fixes had no release vehicle. The operator decided on 2026-11-20: corrections are minor, and the security subset ships in 5.x instead of a v6 train.

## Decision

- **Rule 1, corrections are minor.** A fix to a stable tool that keeps the response shape and error categories and only replaces an incorrect result with the correct one ships in a minor release as `Fixed` with a changelog note. A new or changed shape, error category, or published message text is a major change.
- **Rule 2, removing leaked caller input in a minor.** Removing caller input, paths, or filter text that a stable response or documented log event exposes may ship in a minor release with an ADR and a migration note, with no deprecation window. Rule 2 overrides the response-shape rule only for the leaked datum itself. Allowed: drop the field that carries it, or redact its value; a redacted value must still satisfy the advertised output schema (otherwise it is a shape change and major). Replacement text for a redacted published message is permitted. Everything else in that response stays major, as does removing sensitive detail outside this scope (secrets, stack traces).
- **Who classifies.** The implementing agent qualifies each row against both rules with before/after wire evidence (raw request and response on the base commit and on the change, every supported protocol era). A row that fails Rule 1 and does not qualify for Rule 2 is re-parked as major-only with the evidence quoted in the row.
- **Worked examples.** A count that omitted partial-class members now includes them with the same fields and types: Rule 1. A fix that adds a `partial` field or turns a success into `InvalidOperation`: major. A refusal that echoes the caller's filter string drops the string: Rule 2. The same PR renaming its error category: major.

## Compatibility

| Area | Effect |
|---|---|
| Stable contract, Rule 1 fix | No shape change; incorrect values change to correct ones |
| Stable contract, Rule 2 fix | Leaked field removed or value redacted; consumers must not depend on it |
| Everything else | Major release, ADR, migration note |
| Deprecation window | None for Rule 2 |
| Protocol eras | Both tested on the raw wire |

## Migration

- Do not read leaked fields or published message text.
- Branch on `category`.
- Ignore unknown or absent optional fields.

## Consequences

- The release-impact-uncertain rows and the rows `cross-project-public-refusals-echo-input`, `native-test-filter-public-projection` and `executed-log-line-leaks-filter-and-paths` may ship in 5.x when they qualify under Rule 1 or Rule 2; the others stay parked.
- ADR 0011's "deprecate for one minor" line is unchanged.
