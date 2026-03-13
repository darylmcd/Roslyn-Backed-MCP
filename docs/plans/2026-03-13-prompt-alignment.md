# Prompt Alignment Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Align the Roslyn MCP server's public contract and internals with the original build prompt, especially around explicit workspace sessions, tool inputs, diagnostics shape, and prompt-accurate docs/tests.

**Architecture:** Replace the singleton implicit workspace model with a session-aware workspace manager that owns one or more loaded Roslyn workspaces keyed by `workspaceId`. Update the tool/service boundary to use stable request DTOs that support either source locations or metadata-name targeting where the prompt requires it, while preserving preview-token safety via session-version checks.

**Tech Stack:** .NET 10, ModelContextProtocol C# SDK, MSBuildWorkspace, Roslyn public APIs, MSTest

---

### Task 1: Add contract tests for prompt-required workspace sessions

**Files:**
- Modify: `tests/Company.RoslynMcp.Tests/IntegrationTests.cs`
- Modify: `tests/Company.RoslynMcp.Tests/TestBase.cs`
- Test: `tests/Company.RoslynMcp.Tests/IntegrationTests.cs`

**Step 1: Write the failing test**

Add tests that assert:
- `workspace_load`/manager load returns a non-empty `workspaceId`
- `workspace_status` data is keyed by `workspaceId`
- reloading/status for an unknown `workspaceId` fails cleanly

**Step 2: Run test to verify it fails**

Run: `dotnet test RoslynMcp.slnx --filter "FullyQualifiedName~Workspace"`
Expected: FAIL because the current implementation has no `workspaceId` session model.

**Step 3: Write minimal implementation**

Introduce session-aware workspace state and update the manager/status DTOs to carry `workspaceId` and snapshot metadata.

**Step 4: Run test to verify it passes**

Run: `dotnet test RoslynMcp.slnx --filter "FullyQualifiedName~Workspace"`
Expected: PASS

### Task 2: Add contract tests for prompt-required symbol targeting and diagnostics shape

**Files:**
- Modify: `tests/Company.RoslynMcp.Tests/IntegrationTests.cs`
- Modify: `src/Company.RoslynMcp.Core/Models/*.cs`
- Modify: `src/Company.RoslynMcp.Core/Services/*.cs`
- Test: `tests/Company.RoslynMcp.Tests/IntegrationTests.cs`

**Step 1: Write the failing test**

Add tests that assert:
- `symbol_info` supports fully qualified metadata name lookup
- `project_diagnostics` separates workspace/load, compiler, and analyzer diagnostics
- status includes richer prompt-required metadata

**Step 2: Run test to verify it fails**

Run: `dotnet test RoslynMcp.slnx --filter "FullyQualifiedName~Symbol|FullyQualifiedName~Diagnostics"`
Expected: FAIL because the current contract is narrower than the prompt.

**Step 3: Write minimal implementation**

Add request DTOs and service overloads for metadata-name targeting, plus diagnostics/status DTO changes.

**Step 4: Run test to verify it passes**

Run: `dotnet test RoslynMcp.slnx --filter "FullyQualifiedName~Symbol|FullyQualifiedName~Diagnostics"`
Expected: PASS

### Task 3: Refactor tool wrappers and preview-first refactors around `workspaceId`

**Files:**
- Modify: `src/Company.RoslynMcp.Host.Stdio/Tools/*.cs`
- Modify: `src/Company.RoslynMcp.Roslyn/Services/*.cs`
- Modify: `src/Company.RoslynMcp.Core/Services/*.cs`
- Test: `tests/Company.RoslynMcp.Tests/IntegrationTests.cs`

**Step 1: Write the failing test**

Add tests that prove refactor previews/apply and semantic reads are scoped to the correct `workspaceId`.

**Step 2: Run test to verify it fails**

Run: `dotnet test RoslynMcp.slnx --filter "FullyQualifiedName~Refactoring|FullyQualifiedName~Definition|FullyQualifiedName~References"`
Expected: FAIL because the current services use the singleton loaded workspace.

**Step 3: Write minimal implementation**

Thread `workspaceId` through the tool wrappers and service layer, and tie preview tokens to session snapshot versions.

**Step 4: Run test to verify it passes**

Run: `dotnet test RoslynMcp.slnx`
Expected: PASS

### Task 4: Update canonical docs

**Files:**
- Modify: `README.md`

**Step 1: Update docs**

Document the prompt-aligned tool surface, session model, build/test/run commands, and any remaining explicit deferrals.

**Step 2: Verify docs against code**

Run: `dotnet test RoslynMcp.slnx`
Expected: PASS with docs matching implemented behavior.
