# ADR 0001 — Nested `LocationDto` Field Migration For The Location Quartet

- **Status:** Accepted (2026-08-06 operator decision; recorded 2026-08-07)
- **Deciders:** repository owner
- **Supersedes:** nothing
- **Superseded by:** nothing

This is the first Architecture Decision Record in this repository. It establishes `docs/decisions/` as the home for durable, numbered design decisions whose rationale must outlive the pull request that implemented them. Subsequent ADRs continue the `NNNN-kebab-title.md` numbering.

## Context

`RoslynMcp.Core` already defines a reusable span type, `LocationDto` (`src/RoslynMcp.Core/Models/LocationDto.cs`), carrying `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`, `ContainingMember`, `PreviewText`, and `Classification`.

Three sibling response DTOs nevertheless re-declare the same span coordinates as flat positional parameters instead of composing that type:

| DTO | Declaration | Duplicated members |
|---|---|---|
| `SymbolDto` | `src/RoslynMcp.Core/Models/SymbolDto.cs:6-27` | `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn` (all `int?`/`string?`) |
| `DiagnosticDto` | `src/RoslynMcp.Core/Models/DiagnosticDto.cs:6-15` | `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn` (all `int?`/`string?`) |
| `TypeUsageDto` | `src/RoslynMcp.Core/Models/TypeUsageDto.cs:34-42` | `FilePath`, `StartLine`, `StartColumn`, `EndLine`, `EndColumn`, plus `ContainingMember`, `PreviewText`, `Classification` (non-nullable) |

Together with `LocationDto` these form the "location quartet." The duplication costs consistency (nullability and classification semantics drift between the four) and blocks any shared helper that wants to accept "a location" without an overload per DTO.

Two constraints make this more than a mechanical refactor:

1. **These DTOs are the wire shape of stable tool responses.** `docs/release-policy.md` states that "Stable tools/resources keep backward-compatible request and response shapes within a release line" and that "Breaking a stable request/response contract is a major-version change." Replacing the flat fields with a nested object changes the JSON emitted to every consumer of `symbol_search`, `find_references`, `compile_check`, `project_diagnostics`, `find_type_usages`, and their peers.
2. **The blast radius is large.** The backlog item's Evidence records direct removal spanning at least 12 production files and 6 test files. Producers of the quartet span **nine** files, and the sets are per-DTO — an all-DTO count cannot be derived from a single `rg`:

   - **`DiagnosticDto` — eight files** (`rg -l "new DiagnosticDto\(" src`): `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`, `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`, `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`, `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`, `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`, `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs`, `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs`.
   - **`SymbolDto` — one file** (`rg -l "new SymbolDto\(" src`): `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs` — the sole `SymbolDto` producer, which therefore double-duties as a `DiagnosticDto` producer too and is the one overlap between the sets.
   - **`TypeUsageDto` — one file** (`rg -l "new TypeUsageDto\(" src`): `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs:312` — the sole `TypeUsageDto` producer, and absent from the `DiagnosticDto` sweep entirely.

   Nine distinct files in union. A `DiagnosticDto`-only survey misses `MutationAnalysisService.cs` and so cannot be used to scope Stage 1, which appends `Location` to all three DTOs.

An earlier execute-time public-contract review proposed a third path — expose `Location` as a computed, `[JsonIgnore]`-decorated property over the existing flat fields — and rejected it, because a property that is never serialized gives external consumers nothing to adopt: the wire shape would be unchanged, so no consumer could ever migrate off the flat fields, and the deduplication would exist only for in-process C# callers.

## Decision

**Add a serialized, optional, nested `Location` field alongside the existing flat fields. Do not replace them in a major-version break.**

The operator resolved this on 2026-08-06 (via `/defer-unblock`, recorded in `ai_docs/items/core-dto-location-quartet-consolidation-primary.md`). The additive path was chosen over a breaking major bump because it avoids forcing every consumer to migrate on this repository's timeline, while still satisfying Directive #4's ADR-plus-migration-note requirement for a published repository — additive sidesteps the break entirely for the duration of the deprecation window.

A `[JsonIgnore]`-only computed view is explicitly insufficient and is not an acceptable implementation of this decision. `Location` must be serialized.

### 1. Wire name and shape

Each of `SymbolDto`, `DiagnosticDto`, and `TypeUsageDto` gains one new property:

```csharp
LocationDto? Location = null
```

serialized under the JSON name `location` (the host's existing camelCase policy applies; no `[JsonPropertyName]` override). Its value is a complete `LocationDto` object, not a partial projection.

The existing flat `FilePath` / `StartLine` / `StartColumn` / `EndLine` / `EndColumn` properties keep their current names, types, nullability, and values. During the deprecation window a response carries the span **twice** — once flat, once nested — and the two must agree. Producers populate both from a single resolved span; they must never compute them independently.

`TypeUsageDto` additionally already carries `ContainingMember`, `PreviewText`, and `Classification`, which have direct `LocationDto` counterparts. Those flat members follow the same additive-then-deprecate lifecycle as the coordinates. Note the type mismatch to resolve at implementation time: `TypeUsageDto.Classification` is the `TypeUsageClassification` enum while `LocationDto.Classification` is `string?` — Stage 1 must pick one representation for the nested field and document the mapping rather than silently stringifying.

### 2. Null and partial-location semantics

- `Location` is `null` when the producer could not resolve a span at all. This mirrors the existing flat-field pattern on `SymbolDto` and `DiagnosticDto`, where all five coordinates are nullable and, in the no-span-at-all case, are left null together (`src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs:37-46` is the reference example). All-null is not the only observed state, however — the two bullets below cover the partial cases, and both also emit `Location = null`.
- `Location` is never partially populated. `LocationDto`'s coordinate members are non-nullable value types, so a `LocationDto` instance always carries a full span. A producer holding only a file path and no line/column information emits `Location = null` and populates only the flat `FilePath`, rather than fabricating zero coordinates (`src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs:56-67` is in this state).
- Symmetrically, a producer holding line/column coordinates but no resolvable file path also emits `Location = null` and populates only the flat coordinate fields, leaving flat `FilePath` null. `LocationDto.FilePath` is a non-nullable `string` (`src/RoslynMcp.Core/Models/LocationDto.cs:7`), so such a producer cannot construct a `LocationDto` at all. This arises for compiler-diagnostic producers that hold a `Location` whose mapped line span is valid but which belongs to no on-disk document: `src/RoslynMcp.Roslyn/Services/ScriptingService.cs:248-257` (script-text diagnostics) and `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs:83-92` (snippet-text diagnostics) both construct `DiagnosticDto` with `FilePath: null` and populated `StartLine`/`StartColumn`/`EndLine`/`EndColumn`. Stage 1 must not treat these as resolvable spans.
- `TypeUsageDto`'s flat coordinates are non-nullable today, so every `TypeUsageDto` can always populate `Location`. `SymbolDto` and `DiagnosticDto` are the two that will legitimately emit `null`.
- Consumers must treat "flat fields present, `Location` null" as valid for the whole deprecation window. Consumers must not infer that a null `Location` means the symbol or diagnostic has no source position.

### 3. Legacy-flat deprecation window

`docs/release-policy.md` requires: "Deprecate stable entries for at least one minor release before removal."

The schedule that follows from that rule:

| Phase | Release | State of flat fields | State of `Location` |
|---|---|---|---|
| Introduce | minor `N` | present, supported, undecorated | present, populated |
| Deprecate | minor `N+1` (at the earliest) | present, `[Obsolete]`, still serialized and still populated | present, populated |
| Remove | next major | removed | sole representation |

The deprecation phase must be called out in `CHANGELOG.md` and in `docs/product-contract.md` when it lands. `[Obsolete]` decoration is a compile-time signal to in-process callers only — it does not change the wire shape, so the fields keep serializing throughout minor `N+1` and any later minors on that line.

### 4. Semantic versioning

- `docs/release-policy.md` states "Adding stable tools/resources/prompts is a minor-version change." This ADR extends that rule to DTO field additions: adding the serialized `Location` field is **minor-version-safe**. Existing consumers that deserialize into their own models ignore an unknown property; existing consumers that read the flat fields see no change.
- Decorating the flat fields `[Obsolete]` is likewise **minor-version-safe** — it emits a build warning for in-process C# consumers but does not break the request or response contract.
- Removing the flat fields **breaks a stable response contract and is therefore a major-version change**, per `docs/release-policy.md`'s "Breaking a stable request/response contract is a major-version change."

The one caveat: a consumer with strict schema validation (rejecting unknown properties) will fail on the additive field. That is a consumer-side strictness choice rather than a contract break, but it must be noted in the release notes for the minor that introduces `Location`.

### 5. Record constructor compatibility

All three DTOs are positional `record` declarations, so parameter order is part of their C# API. The new parameter **must be appended at the end of the positional list with a default value**:

```csharp
public sealed record DiagnosticDto(
    string Id,
    string Message,
    string Severity,
    string Category,
    string? FilePath,
    int? StartLine,
    int? StartColumn,
    int? EndLine,
    int? EndColumn,
    LocationDto? Location = null);
```

This keeps every existing positional construction site — production code and test fixtures alike — compiling unchanged. Two ordering constraints apply:

- `SymbolDto` already ends with three defaulted parameters (`HasGetter`, `HasSetter`, `SetterAccessibility`, at `src/RoslynMcp.Core/Models/SymbolDto.cs:25-27`). `Location` goes after those, not before them; inserting ahead of an existing defaulted parameter would silently rebind positional arguments at call sites that pass them positionally.
- `TypeUsageDto`'s current parameters are all non-defaulted, so appending one defaulted parameter is unambiguous.

Adding a defaulted parameter is source-compatible but **not** binary-compatible: the constructor signature changes, so any pre-built assembly compiled against the old constructor must be recompiled. This repository ships a self-contained host, so there is no in-place binary-swap scenario to protect.

### 6. `with` expressions

Unaffected. C# `with` expressions copy all properties and override only the named subset, so existing `symbol with { Kind = "..." }` sites continue to compile and now carry `Location` through the copy automatically. No call site needs editing.

The one behavioral note: a `with` expression that mutates a flat coordinate (for example `diag with { StartLine = 5 }`) will **not** update the nested `Location`, leaving the two representations disagreeing — which violates the "must agree" rule in section 1. Stage 1 must audit for coordinate-mutating `with` expressions and update both representations at those sites.

### 7. Equality

C# records generate structural equality across **all** properties, including the new one. Once `Location` is populated, two DTO instances that were previously equal (same flat fields) remain equal only if their `Location` values are also equal — `LocationDto` is itself a record, so this compares structurally and behaves correctly for equal spans.

The real risk is asymmetric population: a DTO built by a producer (`Location` populated) will **not** equal a DTO built by a test fixture or a consumer that omits it (`Location = null`), even though every flat field matches.

**This is a breaking change for equality-based consumers even under the "additive" framing.** It affects:

- snapshot and golden-file comparisons over these DTOs,
- `Assert.AreEqual` / collection-equality assertions in the test suite,
- any consumer using these records as dictionary keys or set members,
- de-duplication logic that relies on record equality.

Stage 1 must sweep for equality-dependent call sites and either populate `Location` on both sides or compare on an explicit projection. This risk must appear in the release notes for the introducing minor — it is the single most likely source of surprise from an otherwise additive change.

## Migration Plan

The work is split into bounded stages. **Stage 1 is out of scope for this ADR** — this document records the decision and the plan only; no DTO or producer code changes ship with it.

### Stage 1 — producer-side introduction (future backlog row)

- Append `LocationDto? Location = null` to `SymbolDto`, `DiagnosticDto`, and `TypeUsageDto`.
- Populate it in the **nine** confirmed producers, per DTO (see constraint 2 for the per-DTO `rg` commands — a `DiagnosticDto`-only sweep is NOT sufficient to scope this bullet):
  - `DiagnosticDto` (eight): `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs`, `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs`, `src/RoslynMcp.Roslyn/Services/CompileCheckService.cs`, `src/RoslynMcp.Roslyn/Helpers/DotnetOutputParser.cs`, `src/RoslynMcp.Roslyn/Services/ScriptingService.cs`, `src/RoslynMcp.Roslyn/Services/SnippetAnalysisService.cs`, `src/RoslynMcp.Roslyn/Services/UnresolvedAnalyzerReferenceStripper.cs`, `src/RoslynMcp.Roslyn/Services/WorkspaceDiagnosticsSink.cs`.
  - `SymbolDto` (one): `src/RoslynMcp.Roslyn/Helpers/SymbolMapper.cs` — already in the list above; the sets' only overlap.
  - `TypeUsageDto` (one): `src/RoslynMcp.Roslyn/Services/MutationAnalysisService.cs`.
- Resolve the `TypeUsageDto.Classification` enum-vs-string mismatch called out in section 1.
- Sweep coordinate-mutating `with` expressions (section 6) and equality-dependent assertions (section 7).
- Ship as a minor version; call out the equality caveat and the strict-schema caveat in release notes.

Stage 1 may itself be split further if the producer sweep exceeds the repository's per-initiative file caps. A natural sub-split is one stage per DTO, since the three producers sets barely overlap.

### Stage 2 — consumer-side composition (already filed: `core-dto-location-quartet-consolidation-secondary`)

Composes `LocationDto` into `PropertyWriteDto` and `MutationCallerDto`. That row **stays blocked** until Stage 1 lands — its acceptance ties unblocking to the primary contract being decided *and* the first migration stage shipping. Recording this ADR satisfies the first half only.

### Stage 3 — deprecation and removal (future backlog row)

- No earlier than one minor release after Stage 1: decorate the flat fields `[Obsolete]` on all three DTOs, keep serializing them, and update `docs/product-contract.md`.
- At the next major: remove the flat fields and any producer code that populates them redundantly.

## Consequences

**Positive**

- No consumer is forced to migrate on this repository's timeline; adoption is opt-in for the whole deprecation window.
- A single reusable span type becomes available on the wire, enabling shared consumer-side handling across symbol, diagnostic, and type-usage responses.
- The deprecation window and the major-version removal trigger are both written down, so the eventual break is scheduled rather than discovered.

**Negative**

- Every affected response carries the span twice for at least two minor releases — larger payloads and a redundancy that producers must keep consistent.
- Record structural equality changes shape immediately (section 7), which is the one genuinely breaking edge of an otherwise additive change.
- The full consolidation now spans three releases instead of one.

**Provisional**

The specific field name `Location`, its `LocationDto?` nullability, and the flat-plus-nested duplication shape are design calls made ahead of implementation. Stage 1 may surface a reason to refine them — most plausibly around the `TypeUsageDto.Classification` type mismatch or the equality fallout. If Stage 1 changes any of these, supersede this ADR with a new numbered record rather than editing this one in place.

## References

- `ai_docs/items/core-dto-location-quartet-consolidation-primary.md` — originating backlog item, operator decision, acceptance criteria
- `docs/release-policy.md` — compatibility policy, versioning and deprecation rules
- `docs/product-contract.md` — stable vs. experimental surface tiers
- `src/RoslynMcp.Core/Models/LocationDto.cs` — the target shared type
