# change-signature-class-struct-primary-ctor-add-remove — Preserve construction identities in signature edits

**row:** `change-signature-class-struct-primary-ctor-add-remove` · **pri:** `Medium` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/Services/ChangeSignatureAddRemovePreviewBuilder.cs`
- `src/RoslynMcp.Roslyn/Services/ChangeSignatureService.cs`
- `src/RoslynMcp.Core/Services/IChangeSignatureService.cs`
- `src/RoslynMcp.Host.Stdio/Tools/ChangeSignatureTools.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Refactoring.cs`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs`
- `tests/RoslynMcp.Tests/SurfaceCatalogTests.cs`

## Acceptance

- [ ] Preserve the existing initiative, PR1779, remediation branch, source/index changes, and historical causal evidence. Replan authorizes design/planning; implementation and merge remain separate steps.
- [ ] Support class/struct primary-constructor add/remove with original bound arguments, defaults, substitutions, conversions, evaluation order, and trivia. Preserve record and primary rename/reorder boundaries.
- [ ] Preserve distinct edited-constructor and default-construction identities across supported struct arity crossings, including ordinary constructors sharing the builder. Do not promote an implicit zero constructor into an explicit initializer-running zero constructor or remove an existing explicit zero entrypoint.
- [ ] For last-parameter removal, lower the edited constructor to an ordinary nonzero-arity constructor selected by a fresh, required marker. Rewrite every source call bound to the edited constructor, including `this` chains; retain compiler-owned initializer execution and untouched implicit/explicit zero paths.
- [ ] For adding to an explicit zero constructor, retain the original zero-construction behavior and constructor-selection metadata; share the original body/initialization through one nonzero implementation. Rewritten edited source calls receive the requested argument; untouched default consumers must not evaluate a new caller expression. Preserve bindings against new-parameter shadowing. Do not expand unsupported implicit-constructor target selection.
- [ ] Preserve nonzero/effectful/pure-unproved initializers, static timing, typed values, readonly/ref/partial/generic types, nested owner substitutions, object/collection initializer semantics, expression trees, constructor attributes, caller-info values, and cross-assembly accessibility. Do not copy initialization bodies or add optional compatibility parameters.
- [ ] Keep default/new-zero/new-T/Activator/default serializer/array paths untouched; their runtime values and effects must match the original. Remove all compensation code made unnecessary by explicit-zero promotion; retain independent ordinary-method conditional handling.
- [ ] Refuse concretely unrepresentable edits atomically with authored path-free evidence: retained references to a removed parameter, actual dynamic binding ambiguity, or demonstrated incompatible constructor-selection metadata. Test original-valid `JsonConstructor` cases; unknown computations/attributes/consumers are not automatically unsafe.
- [ ] Emit complete diffs and an explicit generated-support-signature warning. Explain marker/output policy in ADR0026, its index, public descriptions, and the existing fragment. A literal zero-arity selected constructor and unchanged implicit-zero dispatch cannot both be promised.
- [ ] Observe causal failures on old behavior, then fresh implementation/runtime/apply/reload proofs, complete serialized `just ci`, exact-head cold review, and hosted Windows/Linux/SDK-floor checks. Preserve every historical assertion; replace the explicit-zero collision refusal with a positive preservation test.
- [ ] Backlog: sync `ai_docs/backlog.md` only after verified landing; keep this row open during planning.

## Evidence

| Evidence | Observation | Limits |
|---|---|---|
| Historical 2026-09-27 report | Installed4.2.1 refused primary-constructor edits; builder only handled ordinary declarations | Original report used a positional record; record support remains a separate boundary |
| Source re-vet 2026-10-13 | Dirty builder916-938 creates explicit zero constructor;284/399-449/537-582/950-1750/2196-2263 compensate its generic/Activator/zero/host consequences | Source evidence; unpublished work is preserved, not accepted |
| Published PR1779 | Head03d4af4b27af9ff9fb5bc1b871ebc2dd203ea6ba | Hosted green checks cover the published head, not unpublished remediation |
| Preserved working state | Index treeccb59b7db26e33199f2b8c9c4919dad817544a80; builderSHA256a9df3bcc7eaa1387ed490eaa3ca9c7aebd6359eda70008da8d7dc3044a7e9d99; testsSHA256e0dc1b851a8c31aa7a4f9119a9b22bd16e2f7b6f25ec18da4bdbbd05c259a5af | Verified this session before replan; all source changes remain untouched |
| Historical checkpoint | Full57 passed before a later serializer regression; original7/0 became7/7 | Prior claimed counts are historical, not current final validation |
| Current SDK10.0.401 scratch probes | Eight original/carrier observations match; explicit-zero alternative changes serializer/default values and static timing | Representation feasibility only; no implementation/full-gate claim |
| Current extended probes | Carrier preserves explicit-zero7/3/3/3, ref-struct7/0, nested-generic7/0; valid attributed serializer7 becomes a constructor-binding refusal under naive marker transfer | Demonstrates a precise metadata boundary; does not validate the tool's refusal implementation |
| Reverse-boundary probes | Adding to explicit zero by replacing it changes external construction; separated zero/edited entries preserve original output | Scratch experiment; implementation must validate exact attributes/body bindings |
| Runtime artifacts | Session scratch `construction-design/`: original/carrier/direct and add-original/add-direct/add-separated sources/logs; runtime-comparison.json; preservation.json | Never substitute scratch results for repository regression coverage |

## Context

### Design decision

Separate construction identities at their declaration instead of rewriting every default consumer.
Use a dedicated construction planner/emitter; reuse the existing semantic type/alias renderer and proof fixture.

| Original path | Required candidate path |
|---|---|
| Edited nonzero constructor loses its last user parameter | Fresh nonzero marker constructor; update only originally bound edited calls |
| Implicit zero constructor | Remains implicit; no new explicit parameterless constructor |
| Existing explicit zero constructor | Retains behavior and constructor-selection role; rebind its original `this` chain where necessary |
| Explicit zero constructor receives a new parameter | Separate edited and existing runtime-zero entries, sharing one initialization/body implementation |
| Other ordinary overloads, default values, generic factories, serializer default activation | Preserve their original semantic/runtime targets |
| Recognized incompatible selection attribute | Precise original-valid atomic refusal; no fabricated purity or impossibility classification |

Generated marker names must be fresh across all partial parts, nested members, type parameters, and lexical binding contexts.
Use explicitly typed marker arguments with the original assembly/extern-alias identity; no optional marker parameter.

Keep field/property initializers compiler-owned; preserve original type layout, readonly assignment rules, partial ordering, static-constructor policy, and constructor attribute values.
Verify caller-info constants after source insertion/formatting; unchanged text positions are not proof.

| Metadata/body obligation | Required handling |
|---|---|
| Reverse explicit-zero addition | Private nonzero core owns the original body and `this` chain; the added parameter never enters their lexical scope |
| Existing zero-path JsonConstructor | Retain its selection on the runtime-zero wrapper when compatible; the attribute alone is not a refusal reason |
| Opaque consumer attributes | Preserve entrypoint ownership; do not blindly duplicate them across wrappers/core |
| SetsRequiredMembers and compiler contracts | Preserve original and edited caller obligations with independently validated metadata |
| Reflection over intentionally edited signature | Document the changed constructor/marker metadata; preservation is limited to original default/zero activation, not arbitrary enumeration or old-signature invocation |

### Alternatives considered

| Alternative | Rejection |
|---|---|
| Explicit zero constructor plus patches to new-T/Activator/serializers | Unseen consumers cannot be patched; runtime-zero and static behavior already diverge |
| Static factory plus private constructor | Changes construction expression shape; forces initializer/control-flow rewriting and additional binding obligations |
| Lift initializers into assignments/factories | Reimplements compiler ordering and readonly/ref safety; risks duplicated bodies |
| Purity-based zero fast path | Requires unnecessary whole-program proof, misses typed/static/metadata distinctions, and rejects valid unproved computations |
| Require literal zero signature and unchanged default dispatch | Both call origins reach the same runtime entrypoint; the requested distinctions cannot be represented simultaneously |

### Publication and ownership

`change_signature_preview` remains experimental; no wire-schema change is proposed.
ADR0026 must document deliberate generated support members and the edited constructor's signature change; do not promise unchanged reflection over intentionally edited metadata.

The same-mechanism primary/ordinary arity cases form one construction-identity repair; file counts describe the complete scope, not a correctness ceiling.
Global tooling defects and other initiatives remain separate; no source or workflow ownership takeover is authorized here.

### Authoritative references

- Microsoft C# structure-type reference: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/struct
- Microsoft instance-constructor reference: https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/instance-constructors
- System.Text.Json constructor selection/binding: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/immutability
