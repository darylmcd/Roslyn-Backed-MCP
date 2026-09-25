# split-service-with-di-refuse-colliding-inputs-outputs — Refuse partial types and colliding output names

**row:** `split-service-with-di-refuse-colliding-inputs-outputs` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SymbolRefactorService.cs`

## Acceptance

- [ ] A `partial` source type is refused (only one part is analysed; another part's instance constructor would leave the facade with two constructors and null partition fields).
- [ ] A partition whose target file `<TypeName>.cs` already exists, or collides with another mutation in the same preview (e.g. the source file), is refused instead of silently overwritten (preview diff is currently built against an empty string).
- [ ] An existing member named `_{lowerFirst(PartitionType)}` is refused instead of producing CS0102 alongside the generated partition field.
- [ ] Regression cases cover all three.

## Evidence

- Cold review of `split-service-with-di-facade-drops-sibling-types` (2026-09-25, findings 7, 8, 11).
- Partition path: `Path.Combine(context.SourceDirectory, $"{partition.TypeName}.cs")` with `DiffGenerator.GenerateUnifiedDiff(string.Empty, ...)`; `CompositeApplyOrchestrator` then overwrites the file.
- Facade collision check covers parameter names only (`BuildFacadeInjectionMembers`).

## Context

Filed 2026-09-25 while shipping the sibling-types fix. Finding 8 (overwrite) is pre-existing on `main`.
