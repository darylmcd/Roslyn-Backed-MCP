# relocation-consumer-type-reference-forms — Relocation consumer pass misses using static, fully-qualified, alias and cref references to the moved type

**row:** `relocation-consumer-type-reference-forms` · **pri:** `Medium` · **size:** `M` · **verified:** `2ae6a73748e9a2433e002f3ecf78ab90f620c7b9`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] `using static Old.T;` (valid for a class or enum) is retargeted to `New.T`, red-first test (stale import compiles with errors today).
- [ ] Fully-qualified `Old.T` references are rewritten to `New.T`, red-first test.
- [ ] Type aliases `using S = Old.T;` are retargeted, red-first test.
- [ ] A cref to the moved type is retargeted (doc mode on) or the limit is the documented, tested outcome.
- [ ] change_type_namespace_preview is experimental, so no stable-surface rule applies.

## Evidence

- Probes (a)-(d) of the relocation-support-enum-delegate-types slice 1 plan, run on the base commit with a class: all stale (IsUnboundTypeNameSite excludes the memberAccess name).
