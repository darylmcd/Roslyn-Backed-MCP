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

## Mutations

This skill is **read-only**. Package and project edits use **`add_package_reference_preview`**, **`apply_project_mutation`**, etc., only when the user asks to change files.
