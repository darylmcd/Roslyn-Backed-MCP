# extract-type-module-initialization-effects — Decide whether extract_type must preserve module-initializer and generator reactions

**row:** `extract-type-module-initialization-effects` · **pri:** `Defer` · **size:** `—`

## Anchors

- `src/RoslynMcp.Roslyn/Services/TypeExtractionService.cs` — no module-initializer or generator awareness.

## Acceptance

- [ ] Operator decision: adopt (then split into a bounded refusal row with red-first fixtures) or reject.

## Evidence

- The deleted extraction archive observed real but exotic changes: a `[ModuleInitializer]`-observed value changing 7→8 and initializer order AB→BA after extraction. `extract-type-preview-compile-delta-gate` does not detect these because the output compiles.

## Context

- Deferred: real, low frequency, and only detectable with initializer/generator analysis.
