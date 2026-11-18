# ADR 0001 — Nested `LocationDto` Field Migration For The Location Quartet

- **Status:** Accepted (2026-08-06 operator decision; recorded 2026-08-07)
- **Deciders:** repository owner
- **Supersedes:** nothing
- **Superseded by:** nothing; section 2 is amended by [ADR 0011](0011-v5-major-release-contract.md) (5.0 end-span nullability and flat-member removal)
- **Implementation:** Stage 1 and the additive secondary DTO slice landed together through `locationdto-stage1-*` and `core-dto-location-quartet-consolidation-secondary`.

First ADR in this repository; `docs/decisions/` holds durable numbered design decisions (`NNNN-kebab-title.md`). Full history of this record: `git log -- docs/decisions/0001-locationdto-nested-field-migration.md`.

## Context

`RoslynMcp.Core` defines `LocationDto` (`src/RoslynMcp.Core/Models/LocationDto.cs`: `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`, `ContainingMember`, `PreviewText`, `Classification`). Three sibling response DTOs re-declare the same span as flat positional parameters instead of composing it:

| DTO | Duplicated members |
|---|---|
| `SymbolDto` | `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn` (nullable) |
| `DiagnosticDto` | same five (nullable) |
| `TypeUsageDto` | same five (non-nullable) plus `ContainingMember`, `PreviewText`, `Classification` |

Together with `LocationDto` these are the "location quartet". The duplication lets nullability and classification semantics drift and blocks shared helpers that accept "a location".

Constraints:

1. These DTOs are the wire shape of stable tool responses (`symbol_search`, `find_references`, `compile_check`, `project_diagnostics`, `find_type_usages`, peers). `docs/release-policy.md`: stable shapes are backward-compatible within a release line; breaking one is a major-version change.
2. Blast radius is nine producer files across the three flat-field DTOs. This list is the single source for the Stage 1 file set; do not re-derive it from a `DiagnosticDto`-only `rg` (that omits the sole `TypeUsageDto` producer, and the enumeration was wrong twice).
   - `DiagnosticDto` (8): `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`, `Helpers/DotnetOutputParser.cs`, `Services/DiagnosticService.cs`, `CompileCheckService.cs`, `ScriptingService.cs`, `SnippetAnalysisService.cs`, `UnresolvedAnalyzerReferenceStripper.cs`, `WorkspaceDiagnosticsSink.cs`.
   - `SymbolDto` (1): `SymbolMapper.cs` (the only overlap with the `DiagnosticDto` set).
   - `TypeUsageDto` (1): `Services/MutationAnalysisService.cs`.
   - `LocationDto` itself is constructed in `SymbolMapper.cs` and `RecordFieldAdditionService.cs`; those sites already emit it and need no Stage 1 change.

A `[JsonIgnore]` computed `Location` over the flat fields was proposed and rejected: the wire shape would not change, so no consumer could migrate and deduplication would exist only in-process.

## Decision

**Add a serialized, optional, nested `Location` field alongside the existing flat fields. Do not replace them in a major-version break.**

Chosen by the operator on 2026-08-06 (backlog row `core-dto-location-quartet-consolidation-primary`, closed). Additive avoids forcing consumers to migrate on this repository's timeline and satisfies Directive #4's ADR-plus-migration-note requirement. A `[JsonIgnore]`-only view is not an acceptable implementation: `Location` must be serialized.

### 1. Wire name and shape

`SymbolDto`, `DiagnosticDto` and `TypeUsageDto` each gain `LocationDto? Location = null`, serialized as `location` (existing camelCase policy, no override) with a complete `LocationDto` value. Flat span properties keep names, types, nullability and values. During the deprecation window a response carries the span twice and the two must agree; producers populate both from one resolved span.

`TypeUsageDto`'s flat `ContainingMember`, `PreviewText` and `Classification` follow the same additive-then-deprecate lifecycle. `TypeUsageDto.Classification` is the `TypeUsageClassification` enum while `LocationDto.Classification` is `string?`: Stage 1 must pick one representation and document the mapping.

### 2. Null and partial-location semantics

- `Location` is `null` when no span resolves (reference: `WorkspaceDiagnosticsSink.cs:37-46`).
- `Location` is never partially populated (`LocationDto` coordinates are non-nullable). File path only, no line/column: `Location = null`, flat `FilePath` only (`UnresolvedAnalyzerReferenceStripper.cs:56-67`).
- Coordinates but no on-disk file (script and snippet diagnostics: `ScriptingService.cs:248-257`, `SnippetAnalysisService.cs:83-92`): `Location = null`, flat coordinates only, flat `FilePath` null, because `LocationDto.FilePath` is non-nullable.
- `TypeUsageDto` can always populate `Location`; `SymbolDto` and `DiagnosticDto` legitimately emit `null`.
- Consumers must treat "flat fields present, `Location` null" as valid for the whole window and must not read null as "no source position".

### 3. Legacy-flat deprecation window

`docs/release-policy.md` requires one minor of deprecation before removal.

| Phase | Release | Flat fields | `Location` |
|---|---|---|---|
| Introduce | minor `N` | present, supported | present, populated |
| Deprecate | minor `N+1` at the earliest | `[Obsolete]`, still serialized and populated | present, populated |
| Remove | next major | removed | sole representation |

Deprecation must be called out in `CHANGELOG.md` and `docs/product-contract.md`. `[Obsolete]` is a compile-time signal only; the wire shape is unchanged.

### 4. Semantic versioning

Adding the serialized `Location` field is minor-version-safe (extends the "adding stable surface is a minor change" rule to DTO fields). `[Obsolete]` decoration is minor-safe. Removing flat fields is a major change. Caveat for the introducing minor's release notes: consumers with strict schema validation rejecting unknown properties will fail on the additive field.

### 5. Record constructor compatibility

All three are positional records, so the new parameter is appended last with a default (`LocationDto? Location = null`), keeping existing construction sites compiling. `SymbolDto` already ends in defaulted `HasGetter`, `HasSetter`, `SetterAccessibility` (`SymbolDto.cs:25-27`); `Location` goes after them, never before (inserting earlier silently rebinds positional arguments). Source-compatible but not binary-compatible; the self-contained host has no binary-swap scenario.

### 6. `with` expressions

Existing `with` sites compile and carry `Location` through the copy. A `with` that mutates a flat coordinate does not update `Location`, violating the section 1 agreement rule; Stage 1 must audit and update both representations at those sites.

### 7. Equality

Record structural equality now includes `Location`, so a producer-built DTO (populated) no longer equals a fixture-built one (`null`) with matching flat fields. This is a breaking edge of the otherwise additive change for snapshot/golden comparisons, `Assert.AreEqual`, dictionary keys and set members, and equality-based de-duplication. Stage 1 must sweep these call sites (populate both sides or compare on an explicit projection) and call the risk out in release notes.

## Migration Plan

| Stage | Scope | State |
|---|---|---|
| 1 | Append `Location` to the three DTOs; populate in the nine producers above; resolve the `Classification` mismatch; sweep `with` (6) and equality (7); ship as a minor with equality and strict-schema caveats. May split per DTO. | Implemented |
| 2 | Additively compose `LocationDto` into `PropertyWriteDto` and `MutationCallerDto` (the latter's legacy shape exposes only the start point; its nested value carries the full span without new flat end fields). Legacy fields retained. | Implemented |
| 3 | No earlier than one minor after Stage 1: `[Obsolete]` the flat fields, update `docs/product-contract.md`. At the next major: remove flat fields and redundant producer code. | Release-gated |

## Consequences

**Positive:** no forced migration on this repo's timeline; one reusable span type on the wire; deprecation window and major removal trigger are written down.

**Negative:** span duplicated on the wire for at least two minors (payload size, producer consistency); equality shape changes immediately (section 7); consolidation spans three releases.

**Provisional:** the field name `Location`, its `LocationDto?` nullability and the flat-plus-nested shape were set ahead of implementation. If Stage 1 changes any of them (most plausibly the `Classification` mismatch or equality fallout), supersede this ADR with a new numbered record rather than editing it.

## References

- `core-dto-location-quartet-consolidation-primary`: originating backlog row (closed; see `CHANGELOG.md`)
- `docs/release-policy.md`, `docs/product-contract.md`
- `src/RoslynMcp.Core/Models/LocationDto.cs`
