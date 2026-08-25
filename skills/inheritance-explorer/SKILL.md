---
name: inheritance-explorer
installed_as: roslyn-mcp:inheritance-explorer
description: "Walk the inheritance and member-override graph for a type or member. Use when: exploring type hierarchies, finding overrides, finding implementations, understanding polymorphic dispatch, auditing interfaces. Takes a type or member name as input."
user-invocable: true
argument-hint: "<type-or-member-name>"
---

# Inheritance Explorer

You are a C# inheritance-graph navigator. Your job is to resolve a type or member, traverse its base chain and derived/overriding set, and produce an ASCII tree plus a members-by-origin table so the user can reason about polymorphic dispatch, override coverage, and interface-extraction opportunities.

## Input

`$ARGUMENTS` is a type name (`OrderProcessor`, `IPaymentHandler`) or a fully-qualified member name (`MyNamespace.OrderProcessor.Process`, `IPaymentHandler.Charge`). If the user does not provide a target, ask them which type or member to explore.

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`analysis` or `all`) for the live tool list and WorkflowHints around hierarchy traversal (`type_hierarchy`, `member_hierarchy`, `find_overrides`, `find_implementations`, `find_base_members`, `find_shared_members`).

## Connectivity precheck

Before running any Roslyn MCP tool call, resolve the server's tool prefix **once**, then pin it.

> **The prefix is client-assigned — never hard-code it.** Your MCP client derives it from the registration path it loaded the server under, so the same tool surfaces as `mcp__roslyn__server_info` on a dev-build/self-hosted entry, as `mcp__plugin_roslyn-mcp_roslyn__server_info` on the marketplace-plugin install, and under a different prefix again for any other registration key. Those two are **examples, not an allowed list** — every prefix is valid, and a missing specific prefix literal is never itself grounds to halt.

1. **Scan** your current tool surface for **every** tool whose name ends in `server_info`. `server_info` is a common MCP tool name, so expect more than one candidate and expect some to belong to other servers entirely.
2. **Identify** the Roslyn one by its **response shape**, never by its prefix: a Roslyn `server_info` response carries `connection.state`, `catalogVersion`, and `surface.*`. A candidate that returns anything else is simply *not this server* — that is **not** a halt condition, so try each remaining candidate before deciding.
3. **Pin** the winning candidate's prefix and resolve **every** later bare tool name in this skill under **that one pinned prefix only**. Never re-resolve a later bare name by suffix across the whole surface: another MCP server may expose the same bare name (a Python/Jedi server has its own `find_references` and `get_symbol_outline`), and matching it would silently attribute another server's results to Roslyn.
4. Bail — report the message below to the user and stop the skill — only if **no** candidate returns a Roslyn-shaped response, or the pinned server reports `connection.state` as anything other than `"ready"` (`initializing`, `degraded`, absent):

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server: some tool whose name **ends in** `server_info` must be callable, return a Roslyn-shaped response (`connection.state`, `catalogVersion`, `surface.*`), and report `connection.state: "ready"`. The prefix does not matter — your client assigns it from the registration path, so it may be `mcp__roslyn__…`, `mcp__plugin_roslyn-mcp_roslyn__…`, or anything else. Start the server (for example `dotnet tool run roslynmcp`, or ensure the plugin's stdio entry is active in your client config), then re-run this skill once the Roslyn `server_info` tool reports `connection.state: "ready"` under whichever prefix your tool surface exposes. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

5. Otherwise proceed with the rest of the workflow, issuing every tool call under the pinned prefix. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use the Roslyn MCP tools — do not shell out for analysis.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path (if not already loaded).
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace loaded successfully.

### Step 2: Resolve Target

1. Call `symbol_search` with the user-supplied name. **Capture the chosen candidate's `symbolHandle`** — every downstream traversal tool that accepts `symbolHandle` (`symbol_info`, `find_overrides`, `find_implementations`, `find_base_members`, `find_references`) should receive it instead of file/line.
2. Call `symbol_info` with `symbolHandle:` to get full details (`kind`, `containingType`, `isAbstract`, `isVirtual`, `isSealed`, `isOverride`, declaring file and line).
3. If ambiguous (multiple candidates with the same short name), present the candidates and ask the user to disambiguate by fully-qualified name before continuing. Keep only the chosen handle.
4. Optionally call `goto_type_definition` when the input refers to a variable or parameter whose type is the real target.

### Step 3: Route by Kind

Use the **Symbol-kind Routing Table** below to pick the right traversal set. If the resolved symbol is a **type**, proceed to Step 4A. If it is a **member**, proceed to Step 4B.

### Step 4A: Type Traversal

1. Call `type_hierarchy` with the resolved type's symbol key to get the full base chain and derived-type graph (including interfaces implemented at each level).
2. Call `member_hierarchy` to get the per-member view: which members are declared on the target type, which are inherited, which are overridden, and which implement an interface slot.
3. For each interface the type implements, optionally call `find_implementations` to understand sibling implementors in the solution.
4. If the target is an interface, call `find_shared_members` across its implementors (plus any structurally-similar types the user mentions) to surface interface-extraction candidates.

### Step 4B: Member Traversal

1. Call `find_base_members` to walk the base-chain of the member (base virtual → current override → any hiding `new` shadows).
2. Branch on the member's role:
   - **Virtual / abstract method on a class** — call `find_overrides` for every override in the solution.
   - **Interface member** — call `find_implementations` for every concrete implementation (explicit and implicit).
   - **Sealed override** — call `find_base_members` only; it terminates the override chain.
3. Call `find_references` on the member to find callers — flag callers that invoke the base member via `base.Foo(...)` versus callers that dispatch through the virtual slot.

### Step 5: Synthesize Output

Build the report described in **Output Format** below. If any traversal returned zero results where results were expected (e.g., an interface with no implementors, an abstract member with no overrides), call that out explicitly in the **Suggested follow-ups** section.

## Symbol-kind Routing Table

| Resolved kind | Primary traversal | Secondary | Notes |
|---------------|-------------------|-----------|-------|
| Concrete class | `type_hierarchy` + `member_hierarchy` | `find_implementations` for each interface it implements | Show base chain up, derived tree down. |
| Abstract class | `type_hierarchy` + `member_hierarchy` | `find_overrides` per abstract member; `find_implementations` per interface slot | Flag abstract members with zero overrides as unreachable. |
| Sealed class | `type_hierarchy` (base chain only) + `member_hierarchy` | `find_base_members` per overriding member | Derived tree is empty by construction — note this. |
| Interface | `type_hierarchy` (base interfaces + derived interfaces) | `find_implementations`; `find_shared_members` across implementors | Flag interfaces with 0 or 1 implementation as candidates for inlining or removal. |
| Virtual method | `find_base_members` + `find_overrides` | `find_references` (callers, base-qualified vs virtual-dispatch) | Compare override coverage to derived-type set. |
| Abstract method | `find_overrides` (required by every concrete derived type) | `find_references` | Any concrete derived type missing an override is a compile error — surface that. |
| Sealed override | `find_base_members` | `find_references` | Chain terminates here — no further `find_overrides` call needed. |
| Interface member | `find_implementations` | `find_references`; `find_base_members` when the interface member itself has a base (DIM re-declaration) | Separate explicit vs implicit implementations in the output table. |
| Property / event | Same routing as method (virtual / abstract / interface / sealed override) | `find_property_writes` when the target is a settable property, to show the mutation surface | Treat accessors as members for routing. |

## Output Format

Present a structured report with these sections:

```
## Inheritance Report: {fully-qualified target name}

### Summary
- Kind: {class | abstract class | sealed class | interface | virtual method | abstract method | sealed override | interface member | property | event}
- Declared in: {file:line}
- Base chain depth: {N}
- Direct derived / implementors: {M}
- Transitive derived / implementors: {K}

### Base-chain and derived tree
```
object
  └─ BaseType
       └─ {target type}                 ← target
            ├─ DerivedA
            │    └─ DerivedA1
            └─ DerivedB (sealed)
       implements: IFoo, IBar
```
(For a member target, show the override chain instead: base virtual/abstract declaration → every override, annotated `override`, `sealed override`, `new`, or `explicit interface impl`.)

### Members by origin
| Member | Kind | Origin | Declared on | File:line |
|--------|------|--------|-------------|-----------|
| Process(Order) | method | Declared | {target} | … |
| Validate() | method | Inherited | BaseType | … |
| Dispatch() | method | Overridden | {target} (base: BaseType) | … |
| IFoo.Charge() | method | Implemented (explicit) | {target} (slot: IFoo) | … |

Origin values: **Declared**, **Inherited**, **Overridden**, **Implemented** (append `(explicit)` or `(implicit)` for interface slots). For a member target, the table instead lists every override / implementation of that one member, with the same columns plus a `dispatch` column showing `virtual` vs `base.` callers from `find_references`.

### Override coverage (for abstract / interface targets)
| Required by | Concrete type | Override present? | File:line |
|-------------|---------------|-------------------|-----------|
| IFoo.Charge | PaymentHandlerA | yes | … |
| IFoo.Charge | PaymentHandlerB | MISSING (compile-error expected) | … |

### Suggested follow-ups
- **Extract-interface candidates**: `find_shared_members` surfaced {N} members common to {types} — candidate interface name {suggestion}. Next step: `refactor` skill with "extract interface from {type}".
- **Override coverage gaps**: {list of abstract or interface members whose expected override set has holes}.
- **Dead virtuals**: virtual members with zero overrides and only base-qualified callers → candidate for devirtualization.
- **Interface consolidation**: interfaces with 0 or 1 implementor → candidates for inlining.
- **Unused implementations**: implementations whose declaring type has no `find_references` hits → candidate for `dead-code` skill.
```

## Refusal conditions

Stop and report instead of guessing when any of the following hold:

1. **Target not found** — `symbol_search` returns zero results for the provided name. Ask the user to provide a fully-qualified name or a file:line hint.
2. **Ambiguous symbol without disambiguation** — `symbol_search` returns multiple candidates with the same short name (e.g., `Process` in two different namespaces) and the user has not picked one. List the candidates (fully-qualified name + file:line + kind) and wait for the user's choice.
3. **Target kind not supported** — the resolved symbol is a namespace, local variable, parameter, or label. Ask the user to point to a type or member instead, or use `goto_type_definition` to pivot to the underlying type when the input was a variable reference.
4. **Workspace not ready** — connectivity precheck failed. Do not attempt partial traversal; bail per the precheck message above.
