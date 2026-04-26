# Profile Large Solution

<!-- purpose: Run repeatable Roslyn MCP large-solution profiling against a 50+ project solution. -->
<!-- scope: in-repo -->

Use this prompt when the repo needs measured evidence for `workspace-process-pool-or-daemon` or any other large-solution performance decision. The current preferred local target is:

```text
C:\Code-Repo\OrchardCore\OrchardCore.slnx
```

## Preconditions

1. Confirm the target repo exists and is clean:

   ```powershell
   git -C C:\Code-Repo\OrchardCore status --short --branch
   ```

2. Confirm the solution is still large enough:

   ```powershell
   Select-String -Path C:\Code-Repo\OrchardCore\OrchardCore.slnx -Pattern '\.csproj' | Measure-Object
   ```

   Expected as of 2026-04-26: 227 projects in the `.slnx`.

3. Confirm the installed MCP command resolves:

   ```powershell
   Get-Command roslynmcp
   ```

## Run

From `C:\Code-Repo\Roslyn-Backed-MCP`:

```powershell
.\eng\profile-large-solution.ps1 `
  -SolutionPath C:\Code-Repo\OrchardCore\OrchardCore.slnx `
  -Iterations 5 `
  -SymbolQuery ContentItem
```

Use `-NoRestore` only when packages have already been restored and the run needs to avoid counting restore setup. Use `-RunEmitCompile` only for a deliberate slower pass that measures PE emit validation separately.

The script writes ignored artifacts under:

```text
artifacts\large-solution-profiling\<timestamp>\
```

Review both:

- `profile-report.md`
- `profile-results.json`

## Interpretation

Compare the P95 values with `docs/large-solution-profiling-baseline.md`.

- If `workspace_load` P95 is under 2 minutes and navigation/search P95 is within the baseline thresholds, keep `workspace-process-pool-or-daemon` deferred.
- If `workspace_load`, `symbol_search`, `find_references`, or `compile_check` P95 is consistently above threshold, update `ai_docs/backlog.md` with measured values, operation names, solution profile, and a concrete next deliverable.

Do not open daemon/process-pool implementation work from anecdotes or a single failed run. Rerun once after confirming restore state and machine load.
