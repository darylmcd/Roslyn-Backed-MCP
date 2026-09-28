# tool-refusal-public-message-guard — Mechanism fix: nothing stops a new refusal from being thrown as a plain InvalidOperationException, which is redacted to generic text

**row:** `tool-refusal-public-message-guard` · **pri:** `Medium` · **size:** `M` · **deps:** `public-argument-exception-core-move`

## Anchors

- (new) `src/RoslynMcp.Core/Services/InvalidOperationErrors.cs`
- `src/RoslynMcp.Core/Services/PublicInvalidOperationException.cs`
- (new) `tests/RoslynMcp.Tests/InvalidOperationConstructionRatchetTests.cs`

## Acceptance

- [ ] A ratchet test, following the repo's existing ratchet-test pattern, counts plain `new InvalidOperationException(...)` constructions per file under `src/` (syntax-based, not a text grep) against a committed baseline. A count may only fall; a new site or a new file with a site fails the test, and the failure message names the allowed paths. Red-first: a synthetic plain construction in a tool-reachable file fails it.
- [ ] Allowed paths are explicit and greppable: `PublicInvalidOperationException` for caller-visible refusals; a Core factory for deliberately internal or redacted errors that returns an exact `InvalidOperationException`, so the wire `exceptionType` does not change; `UnreachableException` and BCL `ThrowIf*` helpers for impossible states. The `PublicInvalidOperationException` remarks say which path to use.
- [ ] Internal invariants that the argument-error family design (`items/public-argument-exception-core-move.md` § Family design, decision (c)) re-homes to `InvalidOperationException` (`host-config-`, `catalog-` and `roslyn-internal-argument-guards-not-caller-errors`) use the Core factory when this row lands first, or are counted in the baseline when it lands after them.
- [ ] The existing per-site rows listed under Context burn the baseline down; each converted site lowers the committed count in the same PR.
- [ ] Compatibility class: the ratchet itself changes no wire behavior. Its burn-down rows stay minor-compatible only while each converted refusal keeps category `InvalidOperation` and `isError`, so only the message becomes specific (`ToolErrorHandler.cs:152-157`). `exceptionType` follows the BCL-base-name rule that this row's dependency `public-argument-exception-core-move` sets. A burn-down conversion that also changes the category, such as a not-found category for an unknown project, is a major-version change (`docs/release-policy.md:24-26`, `:36`) and is tracked outside the 4.x burn-down (`project-name-not-found-category-next-major`).

## Evidence

- `ToolErrorHandler.cs:151-172`: a non-public `InvalidOperationException` becomes "The operation is not valid in the current state. Check the tool contract and retry." Only `PublicInvalidOperationException` messages survive (`:153-156`).
- At `19ccd61b`: 337 plain `new InvalidOperationException(` sites in 69 files under `src/` (293 in 49 files under `src/RoslynMcp.Roslyn/Services`) against 24 `new PublicInvalidOperationException(` sites. No guard stops a new refusal from being added as a plain exception.
- Retro 2026-09-27: 37 generic-refusal occurrences across change_signature, format_range, create_file, move_type_to_project, add_project_reference, set_project_property, migrate_package, rename_preview and the sanctioned-root boundary. Agents abandoned the semantic tool and hand-edited; all 12 refusal envelopes in the 09-13 retro ended in hand edits.

## Context

- Parent mechanism for the InvalidOperationException refusal rows, which stay as the burn-down: `project-mutation-refusals-public-message`, `cross-project-refactoring-refusals-public-message`, `project-name-not-found-misleading-reload-advice`, and the InvalidOperationException halves of `file-create-and-scaffold-refusals-public-message` and `change-signature-refusals-public-message`.
- ArgumentException counterpart: `argument-exception-public-message-guard` and its family (on main since #1654) ban ArgumentException construction in `src/` through a src-scoped `src/BannedSymbols.txt`. The same ban cannot start with InvalidOperationException: RS0030 would fail the build on the 337 existing sites, and RS0030 is shared by every entry in that file, so downgrading it for a baseline would weaken the ArgumentException ban. The ratchet test is the guard; moving the rule into `src/BannedSymbols.txt` once the baseline is empty is optional.
- The binding-failure half of the same retro issue is `tool-binding-missing-parameter-named`.
- Auto-restore failures (`WorkspaceTools.cs:664`, `:676` at `02db6c49`) are sites of this mechanism; `compile-check-restore-required-handshake` fixes them directly.
- Already fixed: `0da6adab` (#1522), `44ed425f` (#1549), `99a95c6e` (#1612).

## Notes

- 2026-09-28: depends on `public-argument-exception-core-move` because both edit `PublicInvalidOperationException.cs`; sequencing after it lets the remarks and the Core factory follow the public-message marker design that row introduces (its 2026-09-26 re-scope, #1654).
- 2026-09-28: Medium, not High. The ratchet only freezes the count; the burn-down rows under Context fix the retro's 37 generic-refusal occurrences, and its dependency `public-argument-exception-core-move` is Medium (L).
