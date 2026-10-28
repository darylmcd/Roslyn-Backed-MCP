# interface-extraction-parameter-contract-attributes — Copy caller-info and nullable-flow parameter attributes to extracted interfaces

**row:** `interface-extraction-parameter-contract-attributes` · **pri:** `High` · **size:** `M` · **deps:** `cross-project-interface-parameter-semantics,interface-member-projection-shared-emitter`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceExtractionService.cs` — `BuildInterfaceMembers` copies no parameter or return attributes.
- `src/RoslynMcp.Roslyn/Services/CrossProjectRefactoringService.cs` — `CreateInterfaceMember` copies no parameter or return attributes.
- New: shared parameter projection helper and a handler-binding check (with `cross-project-interface-parameter-semantics`).

## Acceptance

- [ ] Red first: `TryGet(string k, [NotNullWhen(true)] out string? v)` and `Log(string m, [CallerMemberName] string c = "")` lose their attributes, silently changing nullable flow and caller info for interface callers.
- [ ] Copy only: Caller{MemberName,LineNumber,FilePath,ArgumentExpression}, InterpolatedStringHandlerArgument, Optional, DefaultParameterValue, DateTimeConstant, DecimalConstant, and attributes in `System.Diagnostics.CodeAnalysis`; never ParamArray, ParamCollection, IsReadOnly or ScopedRef (the modifiers carry those). Copy `[UnscopedRef]` on members and accessors.
- [ ] Handler check when a projected parameter has `InterpolatedStringHandlerArgument`: compile, under the receiving parse options, a synthesized static probe class that re-declares the concrete type's generic enclosing chain with constraints and has two methods, Original (receiver typed as the class) and Projected (receiver typed as the interface). Each takes the non-handler parameters with ref kinds kept, params/defaults/attributes stripped and `dynamic` replaced by `object`, and calls the member with named arguments and `$""` per handler argument (indexers: read into a local, or assign from an extra parameter). Refuse on any error, or unless Original binds the concrete implementation, Projected binds the contract member, and both bind identical ordered `IInterpolatedStringHandlerCreationOperation` constructors, one per handler parameter. Use collision-free generated names.
- [ ] Refusals list all ids (no truncation), before token storage.
- [ ] Tests across the four routes: handler, alternate constructor, dynamic neighbour, overload; a generic method whose type parameter is named `@class`, `Original` or `Projected`; an unused constrained containing type parameter in plain, block, line and primary-constructor layouts.

## Evidence

- 2026-10-27: neither emitter copies attributes. Closed PR1840 implemented this as `InterfaceHandlerBindingValidator`; defects to avoid when re-deriving: `Take(5)` id truncation, source parse options used instead of receiving ones, and a dead `declaration is null` path after `.Single()`.

## Context

- Copying `InterpolatedStringHandlerArgument("")` re-types the handler receiver, which can bind a different handler constructor or none; hence the probe.
