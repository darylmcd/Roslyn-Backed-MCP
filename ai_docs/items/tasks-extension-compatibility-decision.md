# tasks-extension-compatibility-decision — Decide the MCP Tasks extension compatibility posture

**row:** `tasks-extension-compatibility-decision` · **pri:** `Low` · **size:** `M`

## Anchors

- `Directory.Packages.props`
- `src/RoslynMcp.Host.Stdio/Program.cs`

## Acceptance

- [ ] Record whether `ModelContextProtocol.Extensions.Tasks` can be adopted against the pinned SDK 2.x line — package availability, target framework, and whether it ships as a separate package version cadence from the core SDK.
- [ ] Record the client-compatibility posture: what a client that does not understand `tasks/get` sees, and whether task-augmented execution must stay opt-in per call.
- [ ] Land the decision as an ADR-lite note (published surface, Directive #4) that the three execution children can implement against, or record a won't-do with the reason.

## Evidence

The parent row states outright: "SDK 2.x is already pinned; the live blocker is the compatibility decision for the separately packaged Tasks extension." Nothing else in the row can start until that is settled.

## Context

Split from `tasks-extension-slow-ops` (2026-09-02). This is the unblocking child — the other three carry a `deps` edge onto it and are correctly dep-blocked until it lands.

Spec context: tasks are the spec-blessed shape for long-running MCP operations (SEP-2663; official extension under 2026-07-28).


## 2026-09-04 execution re-vet addendum

Decision: adopt the separately packaged Tasks extension against the exact `ModelContextProtocol.Extensions.Tasks 2.2.0` / core `2.2.0` pair. Do not promise future release-cadence lockstep.

Additional acceptance:

- [ ] ADR 0007 records protocol 2026-07-28 plus per-request metadata opt-in; down-level or non-opted calls remain synchronous and unsupported direct `tasks/*` calls retain method-not-found/capability refusal behavior.
- [ ] A host-owned central selector keeps tools synchronous by default and allowlists only named slow operations.
- [ ] Task handles are process-lifetime only; restart recovery is a fresh tool call.
- [ ] Configure finite retention instead of the in-memory store's unbounded default.
- [ ] Review the background runner's exception-object logging and require a safe projection or suppression before runtime adoption.
- [ ] Index the decision in `docs/README.md` and `docs/release-policy.md`, and replace ADR 0003's retired umbrella pointer with live rows.

This documentation-only decision is non-breaking; runtime enablement remains an additive opt-in feature with its own Added fragment and dual-era raw-wire coverage.
