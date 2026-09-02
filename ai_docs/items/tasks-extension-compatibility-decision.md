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
