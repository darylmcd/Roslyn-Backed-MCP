---
name: migrate-package
description: "NuGet package migration. Use when: replacing one NuGet package with another across a solution, upgrading packages, or migrating from deprecated packages. Takes old package name, new package name, and new version as input."
user-invocable: true
argument-hint: "<old-package> <new-package> <version>"
---

# NuGet Package Migration

You are a .NET dependency management specialist. Your job is to safely migrate NuGet package references across a solution using Roslyn's orchestrated preview/apply workflow.

## Input

`$ARGUMENTS` should contain three values: `<old-package> <new-package> <new-version>`

Examples:
- `Newtonsoft.Json System.Text.Json 9.0.0`
- `Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.Logging.Abstractions 10.0.0`
- `Moq NSubstitute 5.0.0`

If the user provides incomplete arguments, ask for the missing values.

## Server discovery

Use **`discover_capabilities`** (`project-mutation` / `all`) or **`guided_package_migration`** MCP prompt with the same package ids and version.

## Workflow

### Step 1: Audit Current State

1. Ensure a workspace is loaded.
2. Call `get_nuget_dependencies` to list all package references.
3. Find the old package: which projects reference it, at what versions.
4. If the old package is not found, report this and stop.

### Step 2: Vulnerability Check

1. Call `nuget_vulnerability_scan` to check if either package has known CVEs.
2. Report any vulnerabilities in the current version.
3. Confirm the new version does not have known vulnerabilities.

### Step 3: Preview Migration

1. Call `migrate_package_preview` with:
   - `oldPackageId`: the package to replace
   - `newPackageId`: the replacement package
   - `newVersion`: the target version
2. Show the preview:
   - Number of projects affected
   - Package reference changes in each `.csproj`
   - Any version conflicts

### Step 4: Apply Migration

After user confirmation:
1. Call `apply_composite_preview` with the preview token.
2. Call `compile_check` to verify the solution still compiles.
3. If compilation fails:
   - Call `project_diagnostics` to identify breaking changes.
   - Report errors with file locations and suggested fixes.
   - Note that API differences between old and new packages may require code changes.

### Step 5: Post-Migration Verification

1. Call `compile_check` for a final verification.
2. If there are test projects, suggest running `test_run` to verify behavior.
3. Call `get_nuget_dependencies` again to confirm the migration is reflected.

## Output Format

```
## Package Migration: {old-package} -> {new-package} {new-version}

### Before
{table: project, old-package, old-version}

### Changes
{list of .csproj modifications}

### After
- Compilation: {pass/fail}
- Projects Updated: {count}
- Errors Introduced: {count}

### Required Code Changes (if any)
{list of breaking API changes with file:line and suggested fix}

### Next Steps
- [ ] Run tests: `test_run`
- [ ] Review any API migration notes
- [ ] Update documentation if public APIs changed
```

## Family Mode — bulk migration with an API map

When the user says "migrate all of Moq to NSubstitute" or "replace Newtonsoft with System.Text.Json everywhere," run family mode:

1. **Inventory.** Call `get_nuget_dependencies` and collect every package whose id matches the old family prefix (e.g., `Moq`, `Moq.Extensions`, `Moq.AutoMocker`). Show the user the set and ask to confirm before proceeding.
2. **API map.** Use a known-mapping table (either built-in for common families below, or provided by the user inline). The map translates old-package identifiers into their new-package equivalents so downstream code edits can be planned.

   Built-in family maps:

   | Old family | New family | Notable translations |
   |------------|-----------|----------------------|
   | `Moq.*` | `NSubstitute.*` | `new Mock<T>()` → `Substitute.For<T>()`; `mock.Setup(x => x.Foo()).Returns(y)` → `mock.Foo().Returns(y)`; `mock.Verify(...)` → `mock.Received().Foo(...)` |
   | `Newtonsoft.Json.*` | `System.Text.Json` (BCL) | `JsonConvert.SerializeObject(x)` → `JsonSerializer.Serialize(x)`; `[JsonProperty("n")]` → `[JsonPropertyName("n")]`; `JObject` → `JsonNode`/`JsonDocument` |
   | `AutoMapper.*` | `Mapster.*` | `IMapper.Map<TDest>(src)` stays; profile registration differs (`TypeAdapterConfig<...>.NewConfig()`) |
   | `FluentAssertions.*` | `Shouldly.*` | `x.Should().Be(y)` → `x.ShouldBe(y)`; `x.Should().Throw<T>()` → `Should.Throw<T>(() => x)` |

3. **Per-package migration.** For each package in the old family, run the single-package workflow (Steps 1-5 above) with the corresponding new-package id. Accumulate per-project preview tokens.
4. **Code edits via `bulk_replace_type_preview`.** After package references are swapped, run `bulk_replace_type_preview` for each type in the API map (e.g., `Moq.Mock<T>` → `NSubstitute.Substitute`). Preview → apply via `apply_composite_preview` → `compile_check` per step.
5. **Residual manual edits.** Report any remaining diagnostics from `project_diagnostics` with suggested fixes — family migrations rarely reach 100% automation, and some call-site rewrites need human judgment.
6. **Verify tests still green.** Run `test_run` after each apply.

User-provided maps override built-in ones. Ask the user for mappings when migrating a family not in the table above.

## Guidelines

- Package migrations can introduce breaking API changes. Always compile-check after.
- If the old and new packages have different API surfaces, note that code changes may be needed beyond the package swap.
- For major version upgrades of the same package, use `migrate_package_preview` — it handles the version bump across all projects.
- Family-mode migrations (built-in or user-provided map) multiply blast radius — always run the full Workflow per-package, don't skip verification between steps.
