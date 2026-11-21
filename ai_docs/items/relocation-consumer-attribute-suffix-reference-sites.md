# relocation-consumer-attribute-suffix-reference-sites — Match attribute names without the Attribute suffix as moved-type reference sites

**row:** `relocation-consumer-attribute-suffix-reference-sites` · **pri:** `Medium` · **size:** `S` · **verified:** `881fe36b1f0d0d5a00d5a59b8485bd74e2ef8999`

## Anchors

- `src/RoslynMcp.Roslyn/Services/NamespaceRelocationService.cs`
- `tests/RoslynMcp.Tests/NamespaceRelocationTests.cs`

## Acceptance

- [ ] A consumer that uses the moved attribute type as `[Selected]` (type `SelectedAttribute`) with `using Old;` keeps compiling after change_type_namespace: it gains `using New;` (red-first test; same-mechanism check for `typeof(...)`, `nameof(...)` and arity-differing names).

## Evidence

- Cold review of the reloc PR-B (consumer-scope identity) follow-up 1: IsUnboundTypeNameSite and the mentionsType pre-filter compare the identifier to the type name exactly, so the attribute suffix form is not a reference site. Pre-existing before that PR; arity is also ignored (minor).
