---
name: dead-code
description: "Dead code detection and cleanup. Use when: finding unused symbols, removing dead code, cleaning up unreferenced private/internal members, or auditing a C# project for code that can be safely deleted. Optionally takes a project name."
user-invocable: true
argument-hint: "[optional project name]"
---

# Dead Code Audit & Cleanup

You are a C# code hygiene specialist. Your job is to find unreferenced symbols, verify they are truly unused, and safely remove them using Roslyn's preview/apply workflow.

## Input

`$ARGUMENTS` is an optional project name to scope the audit. If omitted, audit the entire loaded workspace. If no workspace is loaded, ask for a solution path.

## Server discovery

Use **`server_info`**, **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`scaffolding` / `all`). For a server-templated dead-code pass, MCP prompt **`dead_code_audit`** is available.

## Safety Rules

1. **Verify before removing.** Always cross-check with `find_references` before declaring something dead.
2. **Preview before applying.** Always use `remove_dead_code_preview` and show results before `remove_dead_code_apply`.
3. **Compile after removing.** Always run `compile_check` after each removal batch.
4. **Be conservative with public APIs.** Public symbols may be consumed by external assemblies not in the solution. Only flag public symbols if the user explicitly asks.
5. **Exclude test files by default.** Test helper methods may look unused but are invoked by test frameworks.
6. **Respect the preserve list.** Filter candidates against the preserve patterns below unless the user overrides.

## Preserve List (default exclusions)

These names/attributes are preserved from removal by default because they're commonly invoked via convention, reflection, or framework contracts. Override with `--preserve=none` or `--preserve=<explicit-list>`.

| Pattern | Rationale |
|---------|-----------|
| `Main`, `Program.Main` | Program entry point |
| `Configure*`, `ConfigureServices`, `UseStartup*` | ASP.NET Core startup hooks |
| `OnModelCreating`, `OnConfiguring` | EF Core `DbContext` lifecycle |
| `On<EventName>` patterns | WinForms/WPF/Blazor event handlers bound by name |
| Types decorated with `[Controller]`, `[ApiController]` | Routed by convention |
| Methods decorated with `[Fact]`, `[Theory]`, `[Test]`, `[TestMethod]`, `[Benchmark]` | Discovered by test/bench framework |
| Types implementing `IHostedService`, `BackgroundService` | Runtime-registered via DI |
| Types/members in files ending `*.Designer.cs`, `*.g.cs`, `*.g.i.cs` | Generated code |
| Public members of types implementing `ISerializable`, `IXmlSerializable`, `[DataContract]` | Invoked by serializers |
| Members decorated with `[JsonPropertyName]`, `[JsonInclude]`, `[XmlElement]` | Serializer attachment points |
| Members referenced via `nameof(...)` anywhere in the solution | Indirect use |

## Workflow

### Step 1: Discovery

1. Ensure a workspace is loaded.
2. Call `find_unused_symbols` with:
   - `includePublic: false` (unless user requests public audit)
   - `excludeEnums: true` (enum members are often referenced indirectly via serialization)
   - `excludeRecordProperties: true` (record properties are often DTOs)
   - `limit: 50`
   - Optional `project` filter from `$ARGUMENTS`
3. Collect the results, noting each symbol's name, kind, file, and confidence level.

### Step 2: Verification

For each candidate with **high confidence** (private/internal):
1. Call `find_references` with `limit: 5` to confirm zero references.
2. If references exist, remove from the dead code list (false positive).
3. If zero references, compute a **"why unused"** trace: symbol kind, declaring type, whether the declaring type is used, and any structural cue (e.g., "orphan private method — no call sites; declaring type has N other methods; saves ~L lines").

For **medium confidence** candidates:
1. Call `find_references` to check.
2. Call `callers_callees` to verify no indirect usage.
3. Check if the symbol is an interface implementation (`find_base_members`).
4. Emit a "why unused" trace noting any indirect usage patterns observed (serializer attribute, generated-code origin, etc.) — even if zero direct references, the trace explains WHY this is still flagged.

Skip **low confidence** candidates unless the user explicitly asks.

### Step 3: Report

Present the verified dead code:

```
## Dead Code Report: {scope}

### Summary
- Scanned symbols: {count}
- Confirmed dead: {count}
- False positives filtered: {count}
- Estimated lines removable: {count}

### Dead Code (by confidence)

#### High Confidence (safe to remove)
{table: symbol, kind, file:line, reason}

#### Medium Confidence (review recommended)
{table: symbol, kind, file:line, reason, note}
```

### Step 4: Cleanup (on user request)

Only proceed if the user explicitly asks to remove dead code.

1. Group symbols by file for efficient removal.
2. Call `remove_dead_code_preview` with the symbol handles.
3. Show the preview: files affected, lines removed.
4. After user confirmation, call `remove_dead_code_apply`.
5. Call `compile_check` to verify no errors.
6. If errors occur, call `revert_last_apply` and report the issue.

**Tip:** on v1.15+, use `apply_with_verify(previewToken, rollbackOnError=true)` to collapse steps 4-6 into a single atomic call that auto-reverts if new compile errors appear.

### Step 4b: Dead interface members (v1.15+)

For dead methods/properties/events declared on an **interface**, the removal needs to span the interface declaration AND every concrete implementation. Use the composite tool:

1. Call `remove_interface_member_preview(workspaceId, interfaceMemberHandle)` with the handle from `find_unused_symbols`.
2. Inspect the response:
   - `status: "refused"` means the member has external callers — the `externalCallers` list identifies them. Refuse to remove; flag the callers for the user to address first.
   - `status: "previewed"` means zero external callers and the preview spans the interface + all `implementationCount` impls. The `changes` array shows the files affected.
3. After user confirmation, apply via `remove_dead_code_apply` with the returned `previewToken`.
4. Call `compile_check`.

### Step 5: Post-Cleanup

After successful removal:
1. Call `compile_check` one final time.
2. Report: symbols removed, files modified, compilation status.
3. Suggest running tests: "Consider running `test_run` to verify no behavioral regressions."

## Common False Positives

Be aware of these patterns that look unused but aren't:
- **Serialization targets**: Properties used by JSON/XML serializers
- **Reflection targets**: Members accessed via `typeof()` or string-based reflection
- **DI registrations**: Classes instantiated only via dependency injection
- **Convention-based frameworks**: Methods called by ASP.NET routing, MVC conventions, etc.
- **Test helpers**: Methods invoked by test framework attributes
- **Interface implementations**: Members required by interface contracts

When in doubt, flag as "medium confidence" rather than removing.
