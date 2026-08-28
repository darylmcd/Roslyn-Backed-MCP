# breaking-fragment-major-escalation-unconfirmed — Breaking fragment silently escalates to major

**row:** `breaking-fragment-major-escalation-unconfirmed` · **pri:** `Medium` · **size:** `S`

## Anchors

- `eng/verify-breaking-version-bump.ps1:85-101` — asymmetric breaking-fragment handling
- `tests/RoslynMcp.Tests/ChangelogFragmentRequirementTests.cs` — existing fragment-contract tests
- `.claude/skills/release-cut/SKILL.md` — Step 1 preflight (optional surfacing site)

## Acceptance

- [ ] A pending `Changed — BREAKING` fragment on a `major` bump is surfaced as a distinct, operator-visible confirmation — not a lone `Write-Host` line buried in verify output.
- [ ] The verifier reports the breaking fragment's **filename and body** at the escalation point, so the operator can judge the break without opening the file.
- [ ] Consistency heuristic: when a breaking fragment shares a filename stem/family with sibling fragments that are uniformly non-breaking, the verifier emits a WARN naming those siblings.
- [ ] Failing→passing test covers: single breaking fragment among non-breaking siblings ⇒ WARN emitted; breaking fragment with no siblings ⇒ no WARN.
- [ ] Existing breaking→major refusal on patch/minor is unchanged (no regression to the blocking arm).

## Evidence

- 2026-08-28 `/release-cut` preflight: `changelog.d/preview-token-route-binding-bulk-family.md` carried `Changed — BREAKING` while its four siblings from the same route-binding initiative (`preview-token-apply-route-binding`, `-extraction-family`, `-fileops-fixall`, `-multifile-codeaction`) were all `Fixed`, and its shipping commit `dce5488d` was `fix(host):`. The mis-classification would have forced 4.0.0 → 5.0.0 on a published package. Caught only by a human reading a category tally; no gate flagged it. Corrected in PR #1390.

## Context

`eng/verify-breaking-version-bump.ps1` handles the breaking→major rule asymmetrically:

| Bump | Breaking fragment present | Behavior |
|---|---|---|
| patch / minor | yes | `Write-Error` + `exit 1` — loud refusal |
| major | yes | `Write-Host "Breaking fragments permit requested major bump"` — silent permit |

The blocking arm is well covered. The permitting arm is the hole: a category that is merely *spelled* correctly is accepted as *warranted*. `verify-changelog-fragments.ps1:27` validates the category against a five-value allowlist and nothing else, so a single mis-typed `category:` line escalates a published package by a full major version with no confirmation step.

Directive #4 puts this repo in contract-care mode (published as `roslyn-mcp@roslyn-mcp-marketplace`). A spurious major burns a version number and signals a break to consumers that never occurred — the inverse error (a real break shipped as minor) is already gated, this one is not.

## Notes

- Scope is the *permitting* arm only. Do not weaken the patch/minor refusal.
- The sibling-family heuristic is advisory (WARN, not error) — a genuinely-breaking lone member of an otherwise-safe family is legitimate and must stay shippable.
- Cheaper alternative if the heuristic proves noisy: drop it and keep only the explicit filename+body echo at the escalation point.
