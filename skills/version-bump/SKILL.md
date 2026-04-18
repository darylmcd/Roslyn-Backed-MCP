---
name: version-bump
description: "Multi-project version bump across a .NET solution. Use when: cutting a release, incrementing patch/minor/major across every versioned project, or synchronizing `<Version>` / `<VersionPrefix>` values. Takes a bump type (patch, minor, or major) as input. Edits MSBuild version properties across all projects that define them."
user-invocable: true
argument-hint: "patch | minor | major"
---

# Multi-Project Version Bump

You are a release engineer. Your job is to discover every project in the loaded solution that carries an MSBuild version property, compute the new version from the requested bump type, preview the mutation per project, and apply the bump atomically after user confirmation.

## Input

`$ARGUMENTS` is one of: `patch`, `minor`, or `major`. If the user does not provide a bump type, ask which one and stop until they answer. Do not guess.

Semver parsing assumes the `MAJOR.MINOR.PATCH` format (trailing `-prerelease` or `+build` segments are stripped from the bump math and re-attached unchanged on the output). If a project's evaluated version does not match this shape, halt and ask the user how to proceed.

## Server discovery

Use **`server_info`**, resource **`roslyn://server/catalog`**, or MCP prompt **`discover_capabilities`** (`project-mutation` or `all`) for the live tool list and **WorkflowHints** covering preview-token flows, `apply_project_mutation`, and MSBuild property evaluation.

## Connectivity precheck

Before running any `mcp__roslyn__*` tool call, probe the server once:

1. Call `mcp__roslyn__server_info` — confirm the response includes `connection.state: "ready"`.
2. If the call fails OR `connection.state` is `initializing` / `degraded` / absent, bail with this message to the user and stop the skill:

   > **Roslyn MCP is not connected.** This skill requires an active Roslyn MCP server. Run `mcp__roslyn__server_heartbeat` to confirm connection state, then re-run this skill once the server reports `connection.state: "ready"`. See the [Connection-state signals reference](https://github.com/darylmcd/Roslyn-Backed-MCP/blob/main/ai_docs/runtime.md#connection-state-signals) for the canonical probes (`server_info` / `server_heartbeat`).

3. If `connection.state` is `"ready"`, proceed with the rest of the workflow. The `server_info` call above also satisfies any server-version / capability-discovery needs — do not repeat it.

## Workflow

Execute these steps in order. Use Roslyn MCP tools for property evaluation and mutation — do not grep `.csproj` files for version strings, because versions defined in `Directory.Packages.props` or other shared prop files are invisible to raw text search.

### Step 1: Load Workspace

1. Call `workspace_load` with the solution or project path. If no workspace is loaded and the user did not supply a path, ask.
2. Store the returned `workspaceId` for all subsequent calls.
3. Call `workspace_status` to confirm the workspace is ready and capture any load-time warnings.

### Step 2: Enumerate Projects

1. Call `project_graph` to get every project in the solution.
2. Collect the project names and file paths for use in the evaluation loop below.

### Step 3: Discover Versioned Projects

For each project from Step 2:

1. Call `evaluate_msbuild_property` for `Version`. This resolves imports, so values defined in a shared props file are returned.
2. If `Version` is empty or unset, call `evaluate_msbuild_property` for `VersionPrefix` as a fallback.
3. Record `(projectName, propertyName, currentValue)` for every project that yields a non-empty result.
4. For auditing, optionally call `get_msbuild_properties` on one representative project to confirm which property is the authoritative source (e.g., whether `Version` is set directly or derived from `VersionPrefix`).

If the set of discovered versions is inconsistent (for example, three projects at `1.2.3` and one at `0.9.0`), stop and ask the user whether to unify them all to one new target version, or to bump each independently relative to its own current value.

### Step 4: Compute New Version

For each discovered project, parse `currentValue` as semver and apply the bump:

- `patch`: `MAJOR.MINOR.PATCH+1`
- `minor`: `MAJOR.(MINOR+1).0`
- `major`: `(MAJOR+1).0.0`

Pre-release and build-metadata suffixes are preserved verbatim on the output. If parsing fails for any project, halt and refuse — do not silently skip it.

### Step 5: Preview Per Project

For each project, call `set_project_property_preview`:

- `projectName`: the project name from the graph
- `propertyName`: whichever of `Version` / `VersionPrefix` was authoritative in Step 3
- `newValue`: the computed new version

Capture the preview token returned by each call. Do not proceed to apply until every project in the set has a valid preview token.

### Step 6: Show Diff and Confirm

Present a summary to the user:

- A table with `Project`, `Property`, `Old Version`, `New Version`
- The total count of projects affected
- The bump type requested

Ask the user for explicit confirmation before applying. If they decline, discard the preview tokens and stop.

### Step 7: Apply

After confirmation, for each preview token from Step 5:

1. Call `apply_project_mutation` with the preview token.
2. Record success or failure per project.

If any apply call fails, report the failure with the project name and stop — do not continue through remaining tokens, because a partial multi-project bump is worse than a full rollback request.

### Step 8: Verify

1. Call `compile_check` to confirm the solution still compiles after all property mutations.
2. If errors surface, surface them and offer the user the option to revert using `revert_last_apply`.

### Step 9: Close Workspace

Call `workspace_close` to release resources.

## Refusal conditions

Refuse (or halt and ask) in these cases:

- **No versioned projects found.** Every project lacks both `<Version>` and `<VersionPrefix>`. Report this and stop — there is nothing to bump.
- **Inconsistent versions across projects with no user guidance.** Example: projects split across `1.2.3` and `0.9.0`. Ask whether to unify or bump independently.
- **Semver parse failure.** The current value does not match `MAJOR.MINOR.PATCH` (with optional pre-release/build tags). Report the offending project and value, and ask the user for the intended target.
- **Bump type missing or invalid.** `$ARGUMENTS` is empty, or is not exactly one of `patch` / `minor` / `major`. Ask for the bump type and stop until answered.
- **Workspace not loaded and no path supplied.** Ask for a solution path.

## Output Format

Present a structured report:

```
## Version Bump Report

### Summary
- Bump type: {patch | minor | major}
- Projects affected: {count}
- Compile status: {pass | fail}

### Changes
| Project            | Property       | Old       | New       |
|--------------------|----------------|-----------|-----------|
| Foo.Core           | Version        | 1.2.3     | 1.2.4     |
| Foo.Cli            | Version        | 1.2.3     | 1.2.4     |
| Foo.Analyzers      | VersionPrefix  | 1.2.3     | 1.2.4     |

### Post-apply verification
- `compile_check`: {pass | fail with error count}
- Follow-up: {e.g., "CHANGELOG entry still pending", "tag not yet pushed"}
```

If compilation fails, list the top errors (file, line, diagnostic ID, message) and recommend `revert_last_apply` before any further edits.
