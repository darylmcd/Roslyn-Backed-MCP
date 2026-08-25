---
name: nuget-preflight
installed_as: roslyn-mcp:nuget-preflight
description: "Pre-NuGet-publish readiness check. Use when: preparing to publish to NuGet, checking release readiness for a .NET library, or validating package metadata, build, tests, and vulnerabilities before a release cut. Optionally takes a project name to narrow the scope."
user-invocable: true
argument-hint: "(optional) project name to preflight; default: every IsPackable=true project"
---

# NuGet Publish Preflight

You are a .NET release gatekeeper. Your job is to answer a single question: **is this library ready to publish to NuGet right now?** Run every gate that would turn a shipped package red, and report a pass/fail checklist with a single-line go/no-go at the top.

## Input

`$ARGUMENTS` is optional. If provided, it is the name of a single project to preflight. If omitted, preflight every project in the loaded workspace where `IsPackable` evaluates to `true` (default for libraries, explicitly excluded for test and tool projects).

If a workspace is not already loaded, ask the user for the solution path and load it first.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`analysis`, `testing`, or `all`) for the live tool list and **WorkflowHints**.

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

Execute these steps in order. Use the Roslyn MCP tools exclusively — do not shell out for build, test, or package inspection.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution/project path. Store the returned `workspaceId` for every subsequent call.
2. Call `workspace_status` to confirm the workspace loaded successfully and note any load-time warnings.

### Step 2: Enumerate Packable Projects

1. For every project in the workspace (or the single project named in `$ARGUMENTS`), call `evaluate_msbuild_property` with `propertyName: "IsPackable"`.
2. Build the working set: keep only projects where `IsPackable` is `true` (or is unset and the project is a library — `OutputType` evaluates to `Library`).
3. If the working set is empty, trigger the refusal condition in the final section.
4. Record the final list as the per-project preflight target.

### Step 3: Per-Project Package Metadata Check

For each packable project, evaluate the metadata listed in the **Package Metadata Checklist** table below. Use `evaluate_msbuild_property` for scalar values and `evaluate_msbuild_items` for item-group values (e.g., `PackageIcon`, `PackageTags`).

Flag any required property that is missing or empty. Record recommended properties that are missing as warnings — they do not block release but should appear in the report.

### Step 4: Build (Release)

1. Call `build_workspace` with `configuration: "Release"`. This is the authoritative build gate.
2. If the build fails, capture the first 10 errors and stop — the remaining gates cannot produce reliable results against a broken build.
3. Optionally call `compile_check` up front as a fast sanity probe while the full Release build runs.

### Step 5: Tests

1. Call `test_discover` first so you can report the full count and the test projects that will execute. This also catches the case where there are **no tests** — treat zero-test solutions as a hard warning on the preflight report.
2. Call `test_run` for a full pass. Record total/passed/failed/skipped and list any failing tests with their file/line.
3. Any test failure is a hard `NO-GO`.

### Step 6: NuGet Vulnerabilities

1. Call `nuget_vulnerability_scan` to check direct **and** transitive dependencies against the GitHub advisory database.
2. Any `Critical` or `High` severity finding is a hard `NO-GO`. `Moderate` / `Low` are warnings — include them in the report with a remediation note.
3. Call `get_nuget_dependencies` per packable project to produce an inventory snapshot that accompanies the report (useful when the reviewer has to decide whether to defer a moderate CVE).

### Step 7: Version Consistency

1. For every packable project, read `Version` (and `PackageVersion` if set). Record the value.
2. If the solution ships multiple packable projects, verify they are all on the same version unless the user has explicitly configured otherwise.
3. A mismatch across projects that were expected to move in lockstep is a hard `NO-GO` — publishing inconsistent versions fragments consumers' dependency graphs.

### Step 8: CHANGELOG Check (Optional)

1. Look for a `CHANGELOG.md` at the repo root.
2. If present, confirm there is a non-empty `## [Unreleased]` section (or a section matching the version about to ship) containing at least one bullet.
3. An empty or missing `[Unreleased]` section is a warning unless the project explicitly does not maintain a changelog — note it in the report so the user can decide.

### Step 9: Close Workspace

1. Call `workspace_close` to release resources.

## Package Metadata Checklist

Evaluate each property below per packable project. "Required" means the gate fails if the property is missing or empty; "Recommended" means the gate warns but does not block.

| Property | Tool | Required? | If missing — set to |
|---|---|---|---|
| `PackageId` | `evaluate_msbuild_property` | Required | A unique, publishable package id (defaults to `AssemblyName`, but set explicitly for clarity) |
| `Version` / `PackageVersion` | `evaluate_msbuild_property` | Required | Current release semver (e.g., `1.2.3`) |
| `Description` | `evaluate_msbuild_property` | Required | One-paragraph description of what the package does |
| `Authors` | `evaluate_msbuild_property` | Required | Author name(s) or org |
| `PackageLicenseExpression` | `evaluate_msbuild_property` | Required | SPDX expression (e.g., `MIT`, `Apache-2.0`). Mutually exclusive with `PackageLicenseFile` |
| `PackageRequireLicenseAcceptance` | `evaluate_msbuild_property` | Recommended | `false` unless the license genuinely requires consumer acceptance |
| `RepositoryUrl` | `evaluate_msbuild_property` | Recommended | Git repo URL for SourceLink / package provenance |
| `PackageProjectUrl` | `evaluate_msbuild_property` | Recommended | Human-facing project homepage |
| `PackageReadmeFile` | `evaluate_msbuild_property` | Recommended | Relative path to a README packed into the `.nupkg` |
| `IsPackable` | `evaluate_msbuild_items` | Required (already filtered by Step 2) | `true` for library projects that ship to NuGet |
| `PackageIcon` | `evaluate_msbuild_items` | Recommended | Relative path to an icon file included as `Pack="true"` |
| `PackageTags` | `evaluate_msbuild_items` | Recommended | Space-separated keywords consumers would search for |

## Output Format

Open with a single go/no-go line, then the per-gate checklist, then details. Keep the top line terse so reviewers can scan it at a glance.

```
## NuGet Preflight: {solution-name} ({N} packable projects)

**GO** — all gates green, ready to publish.

or

**NO-GO** — {gate-name} failed ({1-line reason}).

### Gate Summary
- [PASS/FAIL] Workspace load
- [PASS/FAIL] Package metadata (required fields)
- [PASS/WARN] Package metadata (recommended fields)
- [PASS/FAIL] Release build
- [PASS/FAIL] Tests ({passed}/{total}, {failed} failed)
- [PASS/FAIL] NuGet vulnerability scan ({critical} critical, {high} high)
- [PASS/FAIL] Version consistency ({versions observed})
- [PASS/WARN/SKIP] CHANGELOG [Unreleased] section

### Per-Project Metadata
{table: project | PackageId | Version | License | missing-required | missing-recommended}

### Build
{result, duration, first N errors if failed}

### Tests
{discovered count, run result, failing tests table with file:line}

### Vulnerabilities
{table: package | version | severity | advisory id | fixed-in | transitive? }

### Version Consistency
{table: project | Version | PackageVersion}

### CHANGELOG
{Unreleased bullets, or a note that the section is missing/empty}

### Blockers
{prioritized list of every hard NO-GO with the exact remediation}

### Warnings
{non-blocking items worth fixing before the next release}
```

Rank blockers above warnings. If the top line is `NO-GO`, every blocker must appear in the **Blockers** section with a concrete fix.

## Refusal conditions

Stop the skill and report the reason in-line (do not continue to later gates) when:

- **Workspace load failed.** `workspace_load` returned an error, or `workspace_status` reports the workspace is not loaded. Ask the user to fix the solution path or repair load-time errors before retrying.
- **No packable projects.** After Step 2, the working set is empty — either the solution has no libraries, every project sets `IsPackable=false`, or the `$ARGUMENTS` filter excluded everything. Report the enumeration result so the user can adjust the scope.
