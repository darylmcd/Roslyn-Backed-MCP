---
name: project-inspection
description: "MSBuild and project-file inspection. Use when: debugging TargetFramework, OutputPath, package references, item includes, or evaluated MSBuild properties for a .csproj."
user-invocable: true
argument-hint: "path to .csproj [property or item type]"
---

# MSBuild / Project Inspection

You answer "what does MSBuild think this project is?" using **`evaluate_msbuild_property`**, **`evaluate_msbuild_items`**, and **`get_msbuild_properties`**.

## Input

Resolve:

- **`workspaceId`** (active session)
- Absolute **`projectFilePath`** (`.csproj` or relevant project file)
- Optionally a single **property name** (e.g. `TargetFramework`) or **item type** (e.g. `PackageReference`)

## Server discovery

Use **`roslyn://server/catalog`** under **project-mutation** / **configuration**. MCP prompt **`msbuild_inspection`** emits a checklist for this workspace + project path.

## Workflow

1. Confirm the workspace is loaded and the path is the project the user cares about (SDK-style vs legacy).
2. For one or two values: **`evaluate_msbuild_property`**.
3. For item lists: **`evaluate_msbuild_items`** with the item type.
4. For a broad dump: **`get_msbuild_properties`** (large — filter to what matters).
5. If project files changed on disk: **`workspace_reload`** before trusting compilation or symbol tools.

## Validate project shape

Invoke with `--validate-shape` or ask "is this project set up right?". The skill runs a consistency sweep over one project (or every project in the workspace) and flags common defaults-drift.

For each project, check:

| Check | Default expected | Signal when missing/inconsistent |
|-------|------------------|----------------------------------|
| `TargetFramework` / `TargetFrameworks` | Present; matches solution-wide convention when one exists | Orphan or legacy TFM (e.g., `net462` in a mostly-`net8.0` solution) |
| `Nullable` | `enable` recommended for new code | `disable` or missing on a library in an otherwise-nullable solution |
| `ImplicitUsings` | `enable` for SDK-style projects | Missing / disabled |
| `LangVersion` | Implicit (inherits from TFM) | Pinned to an old version (`latest` or a specific version below what TFM allows) |
| `TreatWarningsAsErrors` | Typically `true` for production libraries | Missing in a library where siblings enable it |
| `GenerateDocumentationFile` | `true` for packable libraries | Missing when `IsPackable=true` |
| `IsPackable` | `false` for test/sample projects | `true` on a project whose name ends `.Tests` / `.Samples` |
| `OutputType` | `Library` by default; `Exe` only for hosts | `Exe` in a project consumed as a package dependency |
| `RootNamespace` vs folder path | Aligned | Drift (package extracts from a folder that doesn't match namespace) |
| `AssemblyName` vs `PackageId` (packable) | Typically equal | Divergent without explicit intent |
| Project references in SDK-style | Use `<ProjectReference>` | Direct `<Reference>` assembly paths indicate legacy style |

Workflow:

1. `evaluate_msbuild_property` for each of the properties above (per project).
2. `evaluate_msbuild_items` for `ProjectReference`, `PackageReference`, `Compile`.
3. Cross-compare across projects in the workspace when the signal is "inconsistent with siblings" (e.g., nullable enabled in 4/5 projects — the odd one out).
4. Produce a per-project checklist with pass/fail and a one-line remediation per fail.

This mode is read-only — it surfaces inconsistencies but does not mutate csproj files.

## Mutations

This skill is **read-only**. Package and project edits use **`add_package_reference_preview`**, **`apply_project_mutation`**, etc., only when the user asks to change files.
