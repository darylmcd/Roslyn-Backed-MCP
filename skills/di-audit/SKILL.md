---
name: di-audit
description: "Audit dependency-injection registrations in a .NET solution. Use when: auditing DI registrations, finding missing bindings, composition-root review, or detecting lifetime mismatches (Singleton depending on Scoped, etc.). Produces a layered report of every registered service, missing interfaces, unresolved implementations, and DI anti-patterns."
user-invocable: true
argument-hint: "(optional) project name or service-type filter"
---

# DI Registration Audit

You are a C# dependency-injection auditor. Your job is to inventory every DI registration in a .NET solution, bucket them by lifetime, cross-check interfaces against implementations, and surface anti-patterns (duplicates, missing interfaces, lifetime mismatches, reflection-based discovery) in a single layered report.

## Input

`$ARGUMENTS` is an optional scope filter. It can be:
- A project name (e.g., `MyApp.Api`) to restrict the audit to one project's composition root.
- A service type or interface name (e.g., `IUserRepository`) to focus the report on a single registration chain.
- Empty, in which case audit the whole solution.

If a workspace is not already loaded, ask the user for the solution/project path and load it first.

## Server discovery

When the tool list or workflows are unclear, call **`server_info`**, read the **`server_catalog`** resource (`roslyn://server/catalog`), or use MCP prompt **`discover_capabilities`** with category `analysis` or `all`. The `get_di_registrations` tool is the primary inventory source — everything else in this skill cross-checks against it.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for analysis.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path.
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace loaded successfully and note any load-time warnings.

### Step 2: Inventory Registrations

1. Call `get_di_registrations` with the workspace ID. If the user supplied a project-name filter via `$ARGUMENTS`, pass it as a project scope.
2. Capture the full registration list: service type, implementation type, lifetime (Singleton / Scoped / Transient / Hosted), registration call (`AddSingleton`, `AddScoped`, `AddTransient`, `AddHostedService`, `AddX<TInterface, TImplementation>`, etc.), and source file:line.
3. If zero registrations are found, invoke the refusal condition for "no DI container detected" below.

### Step 3: Bucket by Lifetime

Group every registration into four buckets:
- **Singleton** — `AddSingleton`, `TryAddSingleton`, `AddSingleton<T>()` self-registrations.
- **Scoped** — `AddScoped`, `TryAddScoped`.
- **Transient** — `AddTransient`, `TryAddTransient`.
- **Hosted** — `AddHostedService<T>()`.

Within each bucket, sub-group by interface-keyed vs. concrete-keyed registration. Note the count per bucket.

### Step 4: Cross-check Interfaces vs Implementations

For each interface-keyed registration:

1. Call `find_implementations` on the interface — enumerate every concrete type in the workspace that implements it.
2. Call `type_hierarchy` on the interface to confirm the inheritance graph (especially for layered abstractions).
3. Compare the registered implementation(s) against discovered implementations:
   - **Interfaces with no registered implementation** — interface declared in the solution but never bound in any `services.Add*` call. Flag these.
   - **Implementations with no resolver** — concrete types that appear in `find_implementations` but are neither registered themselves nor injected anywhere. Confirm by calling `find_references` on the type; if the only references are the type's own declaration/tests, flag it.

For each flagged implementation, call `symbol_info` to capture the declaring file, kind, and accessibility for the report.

### Step 5: Detect Duplicate Registrations

Walk the inventory and group by service type:
- Two or more non-`TryAdd*` calls for the same service type → duplicate; the last one wins and silently overwrites. Flag both call sites.
- A `TryAdd*` followed by a non-try `Add*` for the same service type → ordering-dependent override. Flag as a smell.
- Multiple implementations registered for the same interface with different lifetimes → almost always a bug. Flag.

### Step 6: Detect Lifetime Mismatches

For every Singleton and Hosted-service registration:

1. Call `symbol_info` on the implementation type to get its constructor signature.
2. For each constructor parameter, look up its service type in the bucketed inventory.
3. If a **Singleton depends on a Scoped** service, flag a captive-dependency bug — the Scoped instance will be captured for the process lifetime and will not honor scope boundaries.
4. If a **Singleton depends on a Transient** service that itself depends on a Scoped service, follow the chain with `callers_callees` on the Transient's constructor and flag any Scoped dependency reachable through that chain.
5. For Hosted services, apply the same rules as Singleton (they share lifetime semantics for captive-dependency purposes).

### Step 7: Flag Reflection-based Registrations

1. Call `find_reflection_usages` scoped to the composition-root project (`Program.cs`, `Startup.cs`, or any `*ServiceCollectionExtensions.cs`).
2. Surface any use of `Assembly.GetTypes()`, `Type.GetTypes()`, `Scrutor.Scan`, convention-based scanning, or `Activator.CreateInstance` that feeds into a registration call.
3. For each hit, confirm via `callers_callees` that the reflection result flows into an `IServiceCollection.Add*` call — discard hits that are not DI-related.
4. Excessive reflection-based discovery (more than a handful, or a scan that pulls in an entire assembly) is a smell — note the scope of each scan in the report.

### Step 8: Constructor Injection Spot-check

For the top 10 most-registered service types (by inbound reference count), call `find_references` → filter to constructor parameters. Look for two anti-patterns:
- **`IServiceProvider` injected directly** instead of the needed concrete dependency — a Service Locator smell.
- **Concrete classes injected where an interface exists** — indicates a missing registration or a leaky abstraction.

### Step 9: Summarize and Close

1. Assemble the **Output Format** sections below.
2. Call `workspace_close` to release resources.

## Anti-pattern Checklist

Check every registration against this table; include a row in the report for each hit.

| # | Smell | Signal | Severity |
|---|-------|--------|----------|
| 1 | Concrete registered as self without interface | `AddScoped<FooService>()` with no `IFooService` binding | P2 |
| 2 | Duplicate registration overwrites prior | Two non-try `Add*` calls for the same service type | P1 |
| 3 | Scoped in Singleton dependency chain (captive dependency) | Singleton ctor injects a Scoped (direct or transitive) | P0 |
| 4 | Scoped in Hosted-service dependency chain | `AddHostedService<T>` where `T` ctor injects a Scoped | P0 |
| 5 | `IServiceProvider` injected directly instead of the needed type | Service Locator smell in ctor | P2 |
| 6 | Interface declared but no implementation registered | Interface has `find_implementations` hits but zero `Add*` calls | P1 |
| 7 | Implementation registered but never resolved | Concrete type in DI, zero ctor-parameter references | P2 |
| 8 | Multiple implementations registered for one interface with mixed lifetimes | Same service type, different `Add*` lifetimes | P1 |
| 9 | Excessive reflection-based discovery | `Assembly.GetTypes()` / `Scrutor.Scan` pulling in a whole assembly | P2 |
| 10 | `TryAdd*` ordering bug | `TryAddSingleton` followed by non-try `AddSingleton` for the same type | P2 |

## Output Format

Present a structured report with these sections:

```
## DI Registration Audit: {solution-or-project-name}

### Summary
- Total registrations: {count}
  - Singleton: {count}
  - Scoped: {count}
  - Transient: {count}
  - Hosted: {count}
- Interfaces without implementation: {count}
- Implementations without resolver: {count}
- Lifetime mismatches: {count}
- Reflection-based registrations: {count}
- Duplicate / overwrite registrations: {count}

### Registrations by Lifetime
{four sub-tables, one per bucket: service type, implementation type, registration file:line, call kind}

### Interfaces With No Implementation
{table: interface, declaring file:line, candidate implementations found by `find_implementations` (if any)}

### Implementations With No Resolver
{table: concrete type, declaring file:line, registered? (yes/no), inbound ctor-param references}

### Lifetime Mismatches
{table: outer type, outer lifetime, dependency type, dependency lifetime, chain (ctor → ctor → ...), severity}

### Reflection-based Discovery
{table: scan site file:line, scope (single type / namespace / assembly), target registration call, risk notes}

### Duplicate / Overwrite Registrations
{table: service type, call sites (file:line × N), which one wins, smell category}

### Anti-pattern Hits
{table keyed by the Anti-pattern Checklist #: smell, location, snippet, recommended fix}

### Recommendations
{top 5 concrete next steps, each paired with the suggested Roslyn MCP tool or skill — e.g., `extract-interface` to add a missing abstraction, `refactor` skill to rename a mis-keyed registration, manual edit to change a lifetime}
```

## Refusal conditions

Stop the skill and report to the user when any of the following are true:

1. **Workspace load failed.** `workspace_load` returns an error, or `workspace_status` does not reach a ready state. Do not proceed — ask the user for a different path or to resolve the load error first.
2. **No DI container detected.** `get_di_registrations` returns an empty inventory AND no `Microsoft.Extensions.DependencyInjection`, `Autofac`, `SimpleInjector`, or similar container reference is present in any project's NuGet dependencies. Report that there is nothing to audit and suggest the `analyze` skill for a general solution health check instead.
