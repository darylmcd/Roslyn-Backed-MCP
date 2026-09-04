# ADR 0007 — MCP Tasks extension compatibility

- **Status:** Accepted (2026-09-04)
- **Deciders:** repository maintainers
- **Scope:** `ModelContextProtocol.Extensions.Tasks` adoption for the RoslynMcp stdio host
- **Supersedes:** ADR 0003's deferred Tasks planning pointer
- **Superseded by:** nothing

## Context

RoslynMcp pins `ModelContextProtocol` 2.2.0 and targets .NET 10. It does not currently reference or
configure the separately published Tasks extension. The 2.2.0 Tasks package supports .NET 10 and
declares an exact `ModelContextProtocol (= 2.2.0)` dependency, so the package pair is compatible
with the current host.

The package is a distinct NuGet artifact. Its released 2.0.0, 2.1.0, and 2.2.0 versions have tracked
the corresponding core releases, but that history is not a compatibility guarantee. Every later
upgrade must verify and adopt an exact extension/core pair; this decision makes no future cadence or
lockstep-version promise.

Tasks are an extension of protocol 2026-07-28 rather than a down-level compatibility feature. A
client opts in per request with `io.modelcontextprotocol/tasks` in its request-scoped client
capabilities. The SDK can then return a task handle and serve `tasks/get`, `tasks/update`, and
`tasks/cancel`. Without that per-request opt-in, the ordinary tool result remains the compatible
response.

The SDK defaults are too broad for RoslynMcp without host policy:

- `ExecutionModeSelector` defaults every tool to task-optional;
- `InMemoryMcpTaskStore.DefaultTimeToLive` defaults to unlimited retention;
- task records are in-memory and disappear at process exit;
- the background runner logs exception objects for store, task-scope, and service-scope failures,
  and its fallback failure record can include the exception message.

## Decision

Adopt `ModelContextProtocol.Extensions.Tasks` only as the exact 2.2.0 extension/core pair in the
subsequent runtime implementation. Adoption is additive and opt-in; this ADR does not add package or
runtime wiring.

### Eligibility and fallback

One host-owned `ExecutionModeSelector` controls task eligibility. It returns `Synchronous` by
default and returns `Optional` only for the named slow-operation allowlist. Tool implementations do
not select their own mode, and RoslynMcp does not use `Required` mode.

A call runs as a task only when all three conditions hold:

1. the request uses protocol 2026-07-28 or later;
2. the request metadata opts into `io.modelcontextprotocol/tasks`;
3. the central selector marks that tool `Optional`.

If any condition is absent, `tools/call` follows the existing synchronous path and preserves its
current result and error contract. A down-level direct `tasks/*` request retains the SDK's
method-not-found refusal. A modern direct `tasks/*` request without the extension capability retains
the SDK's capability refusal.

### Retention and restart recovery

Use one process-local `InMemoryMcpTaskStore` for the stdio host and set
`DefaultTimeToLive` explicitly to 24 hours. A task handle remains valid only until the earlier of its
time-to-live expiry or the host process ending. Do not persist handles or task results across
processes in this adoption.

After a restart, clients must treat an old handle as expired and issue a fresh call to the original
tool. The server must not reconstruct work, infer arguments from a handle, or promise resumption.
Changing to durable or cross-process tasks requires a later compatibility and security decision.

### Failure and logging boundary

Do not route the Tasks background runner's exception object or message into RoslynMcp operator logs.
Before runtime enablement, configure a category-specific suppression or a host-owned safe projection
that records only the established benign structure: failure category, correlation identifier when
available, and task-operation stage. Never log task arguments, tool input, absolute paths, exception
type, exception message, or stack trace.

The task-store boundary must also replace an SDK background-infrastructure failure payload with the
same stable, secret-safe public error policy before it is observable from `tasks/get`. Normal tool
failures keep the existing `CallToolResult { isError: true }` contract; task transport must not
reintroduce exception detail removed by the synchronous boundary.

## Compatibility matrix

| Request | Selector | Result |
|---|---|---|
| 2026-07-28+, Tasks opted in | `Optional` | Task handle; client polls `tasks/get`. |
| 2026-07-28+, Tasks not opted in | `Optional` | Existing synchronous tool result. |
| 2025-11-25 or earlier | `Optional` | Existing synchronous tool result. |
| Any supported protocol | `Synchronous` | Existing synchronous tool result, even if the client opts in. |
| Down-level direct `tasks/*` | Not applicable | Method-not-found refusal. |
| Modern direct `tasks/*` without opt-in | Not applicable | Missing-capability refusal. |

## Delivery sequence

| Planning handle | Responsibility |
|---|---|
| `tasks-extension-workspace-load` | Add the exact package pair, finite-retention store, safe failure boundary, selector, and dual-era lifecycle regressions; allowlist `workspace_load` and `workspace_warm`. |
| `tasks-extension-build-test-run` | Extend only the central allowlist to the selected build/test operations and add their lifecycle/cancellation regressions. |
| `tasks-extension-contract-docs` | Publish the actually shipped tool list, opt-in, polling, cancellation, fallback, expiry, and restart contract. |

Runtime delivery must prove modern opted create/poll/result and cancellation, modern non-opted
synchronous behavior, down-level synchronous behavior, direct-method refusals, finite expiry,
restart invalidation, and secret-safe background failures on the raw wire. Existing progress
notifications remain available and no tool-side annotation becomes a second policy source.

## Public-change classification

| Change | Classification | Consumer migration |
|---|---|---|
| This compatibility decision | Documentation-only maintenance | None. Tasks remain disabled. |
| Later exact-package/runtime enablement under this policy | Additive, opt-in minor feature | None for existing calls. Clients that want Tasks must use protocol 2026-07-28+, opt in per request, and poll the returned handle. |
| A future extension/core pair | Undecided | Re-verify package dependency, target frameworks, API/wire changes, license, and supported protocol eras before adoption. |
| Durable or cross-process task handles | Separate product decision | Define persistence, authorization, isolation, retention, and migration before implementation. |

## Consequences

- Clients that do not understand Tasks keep the synchronous contract.
- Only centrally allowlisted slow operations can consume task-store capacity.
- Retention is finite, and restart behavior is explicit rather than accidental.
- The host must neutralize the SDK's exception-object logging and raw infrastructure-message path
  before enabling the extension.
- Package upgrades do not inherit compatibility from this exact 2.2.0 decision.

## References

- [ModelContextProtocol.Extensions.Tasks 2.2.0 on NuGet](https://www.nuget.org/packages/ModelContextProtocol.Extensions.Tasks/2.2.0)
- [C# SDK 2.2.0 Tasks guidance](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/docs/concepts/tasks/tasks.md)
- [C# SDK 2.2.0 task builder and background runner](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Extensions.Tasks/Server/McpTasksBuilderExtensions.cs)
- [C# SDK 2.2.0 in-memory task-store retention](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Extensions.Tasks/Server/InMemoryMcpTaskStore.cs)
- [C# SDK 2.2.0 release notes](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0)
- [ADR 0003 — MCP SDK 2.x wire compatibility](0003-sdk-2x-wire-compatibility.md)
- [ADR 0006 — ModelContextProtocol 2.2 servicing](0006-modelcontextprotocol-2-2-servicing.md)
- [RoslynMcp release policy](../release-policy.md)
