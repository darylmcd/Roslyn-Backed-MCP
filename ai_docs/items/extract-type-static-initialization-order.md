# extract-type-static-initialization-order — Refuse moves that reorder static initializer side effects

**row:** `extract-type-static-initialization-order` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs:58-61` — member partition; no initializer or side-effect analysis.

## Acceptance

- [ ] Red first: `static List<string> Log = new(); static readonly int First = Note("first"); static readonly int Second = Note("second"); static int Note(string s){ Log.Add(s); return 0; }`, extract `Log, Note, Second`, then read `First`: log order becomes `second,first` (was `first,second`) because moved initializers run in the helper type initializer.
- [ ] Constant and side-effect-free initializers stay eligible; no keyword ban.
- [ ] Refusal is typed and happens before token storage.

## Evidence

- 2026-10-27 runtime probe on main's rendered output logged `second,first`.

## Context

- Operator policy (2026-10-15 decision, `ai_docs/plans/20261007T211247Z_backlog-remediate/operator-decisions-20261015.md`; refined 2026-10-16 in the deleted extraction archive): refuse before any token, workspace or disk effect when supported semantic analysis proves loss, or cannot verify preservation, using a typed public `InvalidOperationException` subtype (the archive named it `ExtractionPreservationUnverifiedException`) distinct from proven-loss refusals. Keep the existing namespace; no blanket keyword bans; no automatic relocation. Adding that public refusal type is breaking under contract-care: record it in ADR 0032 (0029 is taken) plus a `Changed — BREAKING` migration note.

## Notes

2026-11-11 remediation: left unselected. Row Context mandates a new public typed refusal subtype (ExtractionPreservationUnverifiedException; breaking under contract-care: ADR 0032 plus a Changed — BREAKING migration note). Release impact is uncertain: the existing ExtractTypeBlockingDependencyException might suffice, but the row operator policy says otherwise. Operator decision needed: reuse the existing exception (minor) vs new public subtype (next major).

Operator decision 2026-11-20, corrected the same day after a source check: ship the refusal in 5.x on the new typed subtype the 2026-10-15 decision called for (`ExtractionPreservationUnverifiedException`, distinct from proven-loss refusals). The subtype is NOT a breaking change: `ToolErrorHandler.cs:634-640` (`WireExceptionType`) emits the class name, or `InvalidOperationException` when the type implements `IPublicMessageException`; the category stays `InvalidOperation` (`:148-170`); `docs/product-contract.md:90-93` documents `exceptionType` as an open string and tells clients to ignore unknown fields. The new value appears only on inputs that succeed with a wrong result today, so no value an existing client receives changes; existing refusals keep `ExtractTypeBlockingDependencyException`. Earlier text in this file calling the subtype breaking, or deferring it to the next major, is superseded. The success-to-refusal change itself is a 5.x correction under row `release-policy-correction-rule-adr`. Source-derived; prove it with a raw-wire test in both protocol eras in the implementing PR. Decide there whether the subtype implements `IPublicMessageException` (authored path-free message, BCL wire name).

Remediation run 2026-11-21: plan written (new ExtractionPreservationUnverifiedException, purity check on moved static initializers, refusal before token storage, dual-era wire test) but NOT implemented. docs/release-policy.md:44 and ADR 0041 line 20 call a success→InvalidOperation fix major; the 2026-11-20 operator decision in this row reads otherwise. Needs: operator confirms the decision overrides ADR 0041 (docs PR amending the policy + ADR 0043 first, then ships as Fixed) or the row is re-parked major-only. Same-mechanism remainder when it ships: instance-field initializers moved into the helper reorder the same way (file a deps-chained row before the first merge).
