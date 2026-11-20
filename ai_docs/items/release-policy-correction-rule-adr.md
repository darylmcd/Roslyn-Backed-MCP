# release-policy-correction-rule-adr — Amend release-policy.md with the correction rule and the minor-release security exception

**row:** `release-policy-correction-rule-adr` · **pri:** `High` · **size:** `S` · **verified:** `9feb0f3a356b435b925407c13240e1a730cfa872`

## Anchors

- `docs/release-policy.md:18-30` (Compatibility Policy)
- (new) `docs/decisions/0041-stable-corrections-and-security-exception.md` (take the next free ADR number)
- `docs/decisions/README.md`

## Acceptance

- [ ] Rule 1 (corrections are minor): a fix to a stable tool that keeps the existing response shape and error categories and only replaces an incorrect result with the correct one ships in a 5.x minor as `Fixed` with a changelog note. A new or changed shape, error category, or published message text stays a major change.
- [ ] Rule 2 (security subset in a minor): removing caller input, paths or filter text that a stable response or documented log event exposes may ship in a 5.x minor with an ADR and a migration note. `docs/release-policy.md:24-26` currently says such removal ships in a major and `:29` only waives the deprecation window, so the text must change; do not rely on `:29` as written.
- [ ] State who classifies: the implementing agent qualifies each row against the rules with before/after wire evidence; a row that fails Rule 1 is re-parked as major-only with the evidence quoted.
- [ ] Changelog fragment; `just verify-ai-docs` and the docs gate pass.

## Evidence

- Operator decision 2026-11-20 (session questions): "Corrections are minor" chosen for the 5.x release-impact-uncertain rows; "Security subset in 5.x" chosen over opening a v6.0.0 train.

## Context

- Dependents (deps cell): the release-impact-uncertain rows unparked on 2026-11-20 and the security rows `cross-project-public-refusals-echo-input`, `native-test-filter-public-projection`, `executed-log-line-leaks-filter-and-paths`.
- No v6.0.0 is scheduled. Rows still marked next major or major-only stay parked.
