# ADR 0043 — A success-to-DTO-refusal correction is a Rule 1 fix

- **Status:** Accepted (2026-11-21)
- **Deciders:** repository maintainers (operator ruling 2026-11-21)
- **Scope:** `docs/release-policy.md` Rule 1 for stable tools
- **Supersedes:** nothing
- **Refines:** ADR 0041
- **Superseded by:** nothing

## Context

ADR 0041 Rule 1 names "turns a success into `InvalidOperation`" as major. Three parked rows (`extract-type-static-initialization-order`, `edit-syntax-check-project-parse-options`, `rebase-changed-document-silent-drop`) replace a silently wrong success with a refusal that the response DTO already carries (`Success:false` plus the existing refusal text). The rule did not say whether that counts.

## Decision

- A fix that turns a silently incorrect success into a refusal reported **inside the existing response DTO** (`Success:false` and the existing refusal fields, no new field, no new error category, no thrown `InvalidOperation`) ships in a minor release as `Fixed` under Rule 1.
- Still major: a thrown `InvalidOperation` (`PublicInvalidOperationException`) from a path that previously succeeded; a new or changed field, error category, or schema; new published message text outside the existing refusal fields.
- The changelog `Fixed` note must name the refusal and the input shape that now refuses. The qualifying agent still supplies wire evidence on both protocol eras.

## Compatibility

| Change | Class |
|---|---|
| Silent wrong success becomes `Success:false` with existing fields | Minor, `Fixed` |
| Silent wrong success becomes thrown `InvalidOperation` | Major |
| New field or error category | Major |

## Migration

- Callers already must check `Success` on every apply/preview result; no new handling is required.
- Inputs that were silently mishandled now return a refusal and must be fixed or retried differently.

## Consequences

- The three parked rows may ship in 5.x if each qualifies with before/after wire evidence.
