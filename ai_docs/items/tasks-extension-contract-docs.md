# tasks-extension-contract-docs — Document the task surface per product-contract tiering

**row:** `tasks-extension-contract-docs` · **pri:** `Low` · **size:** `S` · **deps:** `tasks-extension-build-test-run`

## Anchors

- `ai_docs/runtime.md`
- `README.md`

## Acceptance

- [ ] The new public task contract is documented per product-contract tiering: which tools offer it, how a client opts in, the `tasks/get` polling shape, and cancellation semantics.
- [ ] The docs state the compatibility posture decided by `tasks-extension-compatibility-decision` for clients that do not understand tasks.

## Evidence

Parent acceptance bullet 3: "New public contract surface documented per product-contract tiering." Published package (Directive #4) — a new client-visible protocol surface needs its contract written down.

## Context

Split from `tasks-extension-slow-ops` (2026-09-02). Last in the chain so it documents what actually shipped rather than what was planned.
