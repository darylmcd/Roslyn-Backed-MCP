# extract-type-late-bound-member-identity — Detect reflection, dynamic and nameof uses of extracted members

**row:** `extract-type-late-bound-member-identity` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:529-541`, `:996-997` — references found via `GetSymbolInfo(...).Symbol` only.

## Acceptance

- [ ] Red first: `typeof(F1).GetMethod("Compute", NonPublic | Static).Invoke(...)` after extracting `Compute` throws NullReferenceException.
- [ ] Red first: retained `dynamic self = this; self.Compute()` throws RuntimeBinderException.
- [ ] Red first: retained `nameof(Compute)` (method-group candidate, not rewritten) breaks compilation.
- [ ] Rewrite `nameof` candidates to the helper member.
- [ ] Refuse `Get{Method,Field,Property,Event,Member}` constant-name queries against the source type, and `dynamic` receivers of the source type; moved code using `dynamic` against private source members also refuses (it loses binder access).
- [ ] Unrelated reflection and dynamic uses stay eligible.

## Evidence

- 2026-10-27 runtime probe: reflection NullReferenceException, dynamic RuntimeBinderException. The deleted archive also recorded runtime-binder failures for moved dynamic code.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.

## Notes

2026-11-11 remediation: left unselected. Row Context mandates a new public typed refusal subtype (ExtractionPreservationUnverifiedException; breaking under contract-care: ADR 0032 plus a Changed — BREAKING migration note). Release impact is uncertain: the existing ExtractTypeBlockingDependencyException might suffice, but the row operator policy says otherwise. Operator decision needed: reuse the existing exception (minor) vs new public subtype (next major).

Operator decision 2026-11-20, corrected the same day after a source check: ship the refusal in 5.x on the new typed subtype the 2026-10-15 decision called for (`ExtractionPreservationUnverifiedException`, distinct from proven-loss refusals). The subtype is NOT a breaking change: `ToolErrorHandler.cs:634-640` (`WireExceptionType`) emits the class name, or `InvalidOperationException` when the type implements `IPublicMessageException`; the category stays `InvalidOperation` (`:148-170`); `docs/product-contract.md:90-93` documents `exceptionType` as an open string and tells clients to ignore unknown fields. The new value appears only on inputs that succeed with a wrong result today, so no value an existing client receives changes; existing refusals keep `ExtractTypeBlockingDependencyException`. Earlier text in this file calling the subtype breaking, or deferring it to the next major, is superseded. The success-to-refusal change itself is a 5.x correction under row `release-policy-correction-rule-adr`. Source-derived; prove it with a raw-wire test in both protocol eras in the implementing PR. Decide there whether the subtype implements `IPublicMessageException` (authored path-free message, BCL wire name).
