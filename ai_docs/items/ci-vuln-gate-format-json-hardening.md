# ci-vuln-gate-format-json-hardening — parse `--format json` instead of the human-readable marker

**row:** `ci-vuln-gate-format-json-hardening` · **pri:** `Medium` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml` — the `Audit packages for known vulnerabilities` step (the `if ($audit -match 'has the following vulnerable packages')` gate)

## Acceptance

- [ ] The gate decides on structured JSON (`dotnet package list --vulnerable --include-transitive --format json`, non-empty `vulnerabilities` array per package) rather than substring-matching the English output line.
- [ ] A locale change or SDK wording change to the audit output no longer silently disables the gate; a synthetic-vuln check still trips it.

## Evidence

- Code-quality review of `ci-vuln-audit-gating` (2026-06-19 top-n-remediation): the now-blocking gate keys on the unversioned, non-localized English substring `has the following vulnerable packages`. A dotnet SDK wording change or non-English locale would revert the gate to fail-open (no vuln detected when one exists). The dotnet 10 SDK supports `--format json`, a structured locale-stable signal.

## Context

The vuln-audit gate was made blocking (and hardened against a genuine dotnet error) in the same PR that surfaced this. The substring match was accepted as the minimal correct fix for that row; switching to `--format json` is the follow-on robustness improvement.
