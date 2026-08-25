# editorconfig-private-field-naming-rule-overbroad — re-scope the private-field naming rule

**row:** `editorconfig-private-field-naming-rule-overbroad` · **pri:** `Medium` · **size:** `M`

## Anchors

- `.editorconfig` (lines 41-49: `dotnet_naming_rule.private_fields_should_be_camel_case`)
- `eng/format-baseline.json` (the 306 IDE1006 findings this rule produces)

## Acceptance

- [ ] `dotnet_naming_symbols.private_fields` gains a `required_modifiers` scoping (or an explicit second symbol group) so `private const` and `private static readonly` are not required to carry a `_` prefix, matching the convention the codebase actually follows.
- [ ] `eng/format-baseline.json` regenerated after the rule change; the IDE1006 count drops materially and the new total is recorded in the row/CHANGELOG.
- [ ] No source file is renamed to satisfy the old rule as part of this change.

## Evidence

The rule sets `applicable_kinds = field` + `applicable_accessibilities = private` with NO `required_modifiers`, so it applies to const and static-readonly fields too. Measured 2026-08-25: IDE1006 = 306 of 418 total tracked formatter findings across 232 files. Sibling test files written this sweep (e.g. `MethodDescriptionDietScaffoldingMutationTests.cs:19,27`) use `private const int MaxDescriptionCharacters` — PascalCase — and are flagged, as is essentially every comparable declaration in the repo.

## Context

Surfaced while landing `format-baseline-inventory-contract` (PR #1347). The initial reaction was to rename the new files' consts to `_camelCase`; that was rejected as it would make them inconsistent with the rest of the codebase to satisfy a rule nothing else obeys. The root cause is the rule's scoping, not the call sites. Fixing it is the correct route to shrinking the dominant formatter-debt class rather than baselining it forever.
