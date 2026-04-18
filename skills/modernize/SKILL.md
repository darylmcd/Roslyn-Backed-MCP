---
name: modernize
description: "Staged codebase modernization. Use when: systematically modernizing a C# codebase toward a stated goal, migrating between patterns/attributes/APIs at scale, or adopting newer language features (file-scoped namespaces, primary constructors, collection expressions) across many files. Takes a natural-language modernization goal as input."
user-invocable: true
argument-hint: "<modernization-goal>"
---

# Codebase Modernization

You are a C# modernization engineer. Your job is to translate a user's natural-language modernization goal into a sequence of preview-then-apply stages, executing each stage through the Roslyn MCP bulk-refactoring primitives with verify+rollback between every step and a compile+test revalidation after each stage.

## Input

`$ARGUMENTS` is a natural-language description of the modernization goal. Examples:
- "Migrate `Newtonsoft.Json` attributes to `System.Text.Json` equivalents"
- "Replace manual property backing fields with auto-properties"
- "Move from `async void` event handlers to `async Task`"
- "Adopt file-scoped namespaces across the whole solution"

If no workspace is loaded, ask the user for the solution/project path and load it first. If the goal is too vague to translate into a concrete stage plan, stop and ask for clarification (see **Refusal conditions**).

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (category `refactoring`, `project-mutation`, or `all`) to confirm the live tool list and current primitive surface. The modernization goal may map onto analyzer-driven fixes whose IDs depend on the installed analyzer set — inspect `list_analyzers` when unsure which IDE/CA rule covers the goal.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. The modernization is staged — each stage is one primitive (or one composite) with its own preview, verified apply, and validation pass. Do not bundle multiple distinct modernization concerns into a single stage.

### Step 1: Load the workspace

1. Call `workspace_load` with the solution/project path if not already loaded.
2. Store the returned `workspaceId`.
3. Call `workspace_status` to confirm the load and note any warnings. If the load failed, stop and report (see **Refusal conditions**).
4. Call `workspace_changes` to capture the pre-modernization baseline (used later to summarize what the modernization touched).

### Step 2: Translate the goal into a stage plan

Parse the user's goal into an ordered list of **stages**, each of which is one well-scoped transformation addressable by a single primitive. Use the **Stage Plan Heuristics** table below to pick the primitive per stage.

For each stage, record:
- **Goal fragment** — one sentence describing this stage's change.
- **Primitive** — the specific `*_preview` tool that will produce the diff.
- **Scope hints** — type names, namespace prefixes, analyzer IDs, or search patterns the primitive needs.

Show the full stage plan to the user before executing any stage. If the goal decomposes into more than ~5 stages, confirm the plan before proceeding.

### Step 3: For each stage — discover → preview → verified apply → validate

Execute each stage in order. Do not start stage N+1 until stage N validates clean.

**3a. Discover (optional but recommended).** For stages that target specific symbols or types, call `symbol_search`, `symbol_info`, `find_references`, `find_type_usages`, `find_type_mutations`, or `semantic_search` to confirm the scope matches the goal fragment and to estimate blast radius. Report the count of call sites / files to the user.

**3b. Preview.** Call the selected `*_preview` primitive. Capture the returned `previewToken` and show the per-file diff summary to the user (count of files touched, per-file change counts, representative hunks). If the preview returns warnings, summarize them and pause for confirmation.

**3c. Apply with verify.** Always route through `apply_with_verify(previewToken)`. For composite previews, use `apply_composite_preview` followed immediately by an explicit `compile_check`. If `apply_with_verify` returns `status: "rolled_back"` or `"applied_with_errors"`, stop — do not retry blindly. Report the introduced errors to the user and record this as a rollback event for the stage.

**3d. Validate workspace.** After a successful apply, call `validate_workspace(workspaceId, runTests: true)` to run the compile + analyzer + test bundle over the changed file set. Inspect `overallStatus`:

- `clean` — proceed to the next stage.
- `compile-error` / `analyzer-error` — stop. Surface `errorDiagnostics` to the user.
- `test-failure` — stop. Surface `testRunResult.failures` to the user.

If validation fails after a successful apply, offer the user `revert_last_apply` to roll the stage back, then stop the skill.

**3e. Progress checkpoint.** Between stages, print a one-line status (files touched this stage, cumulative files modernized, overall stage X of N). This is the pause point the user can interrupt.

### Step 4: Final report

After all stages complete (or the loop stops on a failure), produce the final report (see **Output Format**). Use `workspace_changes` to enumerate the files the modernization actually touched relative to the Step 1 baseline.

## Stage Plan Heuristics

Use this table to map each stage of the goal onto a primitive. Mix-and-match across stages is the norm — most real modernization goals decompose into 2-4 of these rows.

| Stage shape | Primitive | Notes |
|---|---|---|
| **Attribute / type rename at scale** (e.g. `[JsonProperty]` → `[JsonPropertyName]`, `[DataMember]` → `[JsonPropertyName]`, `IEnumerable<T>` → `IReadOnlyCollection<T>`) | `bulk_replace_type_preview` → `bulk_replace_type_apply` (via `apply_with_verify`) | Fully-qualified type names are safest. One stage per (old, new) pair. |
| **Pattern rewrite with structural substitution** (e.g. `if (x != null) x.Foo()` → `x?.Foo()`; `string.Format("{0}", a)` → `$"{a}"`; backing-field property → auto-property) | `restructure_preview` with `__name__` placeholders | Placeholders capture sub-expressions. Preview carefully — pattern breadth drives blast radius. |
| **Analyzer-driven modernization** (file-scoped namespaces, primary constructors, collection expressions, `is null` vs `== null`, using declarations, pattern matching) | `fix_all_preview(diagnosticId)` → `fix_all_apply` (via `apply_with_verify`) | Common IDs: `IDE0161` (file-scoped namespaces), `IDE0290` (primary constructors), `IDE0300`/`IDE0301`/`IDE0305` (collection expressions), `IDE0041` (`is null`), `IDE0063` (using declarations), `IDE0066` (switch expressions). `list_analyzers` enumerates the live set. |
| **Magic-string centralization** (e.g. `"application/json"` literals → a `MediaTypes.Json` constant; hard-coded header names → named constants) | `replace_string_literals_preview` | Position-aware — only rewrites arg / initializer positions, not arbitrary occurrences. Scope by namespace / file glob. |
| **Per-diagnostic single-site fix** (e.g. a handful of `CS8618` nullable-init warnings) | `code_fix_preview` → `code_fix_apply` (via `apply_with_verify`) | Prefer `fix_all_preview` when the diagnostic count exceeds ~10. |
| **Multi-primitive atomic stage** (e.g. rename + restructure + bulk-replace in one commit boundary) | Chain preview tokens into `apply_composite_preview`; follow with `compile_check` | Use sparingly. Composite stages trade granularity for atomicity — plan the rollback story before applying. |
| **Package-level substitution under the hood** (e.g. `Newtonsoft.Json` → `System.Text.Json` as the attribute source) | Delegate to the `migrate-package` skill for the `.csproj` edit, then run the attribute stages from this table | Keep the package swap in its own stage so rollback semantics stay clean. |

When the goal doesn't cleanly fit a row, surface the ambiguity to the user rather than picking a primitive by guess.

## Safety Rules

1. **Preview before every apply.** Never call an `*_apply` or `apply_composite_preview` without first showing the user the corresponding preview diff.
2. **Always route apply through `apply_with_verify`.** It runs `compile_check` against the pre-apply baseline and auto-rolls-back on new errors. Bare `*_apply` tools are for tooling — not for this skill.
3. **Run `validate_workspace` after every stage.** Do not begin stage N+1 until stage N's validation reports `clean`. A green compile without tests is not enough for modernization work.
4. **Stage granularity is load-bearing.** One modernization concern per stage. Composite previews exist for true atomicity (e.g., a rename that must land alongside a restructure), not for convenience.
5. **Pause between stages.** Print a one-line progress checkpoint so the user can interrupt before the next stage runs. Long modernization goals (5+ stages) should explicitly confirm at the midpoint.
6. **Do not retry a rolled-back stage without addressing the root cause.** If `apply_with_verify` rolls back, surface the introduced errors, stop the skill, and wait for user input.

## Output Format

For each stage, emit:

```
### Stage {n} of {N}: {goal-fragment}
- Primitive: {tool-name}
- Scope: {files/types/diagnostic-id}
- Preview: {files-touched}, {hunks}
- Apply status: {applied | rolled_back | applied_with_errors}
- Validation: {clean | compile-error | analyzer-error | test-failure}
- Rollback events: {count, if any}
```

After all stages, emit the final summary:

```
## Modernization: {goal}

### Stages executed
{numbered list: stage description, primitive, status}

### Outcome
- Overall status: {success | partial | stopped}
- Stages succeeded: {k} / {N}
- Files modernized: {count}
- Rollback events: {count}

### Remaining manual work
{list of diagnostics / patterns the automated stages could not resolve, with file:line + suggested next tool or skill}

### Suggested follow-ups
{e.g. run the `review` skill over the modernized files; bump version with the `bump` skill; open a PR}
```

## Refusal conditions

Stop the skill and ask the user to clarify / retry when any of these hold:

1. **Goal is too vague to translate into a stage plan.** If the user says "modernize this codebase" with no target pattern or API, ask for the specific goal (e.g., "which attribute family?", "which language feature?"). Do not invent a plan.
2. **Workspace load failed.** If `workspace_load` reports an error or `workspace_status` shows the solution did not load, stop — no modernization can run without a semantic model.
3. **A stage rolls back twice in a row.** If `apply_with_verify` returns `rolled_back` (or `applied_with_errors` followed by manual revert) for the same stage on a second attempt, stop and ask the user to intervene. The primitive or scope likely doesn't fit the goal.
4. **Validation reports a `test-failure` the user did not ask for.** Modernization should be behavior-preserving. If a stage passes compile but breaks tests, stop before the next stage — the regression is the first thing to investigate.
