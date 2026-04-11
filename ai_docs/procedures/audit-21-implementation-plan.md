# AUDIT-21 implementation plan

<!-- purpose: Detailed implementation plan for closing AUDIT-21 — host-injected IDE/CA analyzers in MSBuildWorkspace. For review before work begins. -->

| Field | Value |
|-------|-------|
| **Status** | Draft — pending review |
| **Created** | 2026-04-08 |
| **Owner** | TBD |
| **Tracking** | `ai_docs/architecture.md` § Known Gaps; `docs/parity-gap-implementation-plan.md` § Known architecture limitation |
| **Risk** | Medium (reflection against unsanctioned API surface; behavioral change to every diagnostic-touching tool) |
| **Reversibility** | Fully reversible behind a feature flag (`ROSLYNMCP_HOST_ANALYZERS`) |

---

## 1. Executive summary

AUDIT-21 documents a known gap: `MSBuildWorkspace` does not surface the IDE-side analyzer set (`IDE0xxx` rules and their code-fix providers) that ships in `Microsoft.CodeAnalysis.Features.dll` / `Microsoft.CodeAnalysis.CSharp.Features.dll`. Those assemblies are **already loaded** in the host process — `RoslynMcp.Roslyn.csproj:9-10` references both — but nothing in the diagnostic pipeline injects their analyzers into the per-project compilations the server analyzes.

This plan adds a single, reflection-based provider that discovers the host-loaded `DiagnosticAnalyzer` types and merges them into `CompilationCache.BuildCompilationWithAnalyzersAsync`. Code-fix providers from the same assemblies are already loaded by `CodeActionService.LoadCodeFixProviders` (`CodeActionService.cs:210-240`) — which is the exact reflection template this plan reuses. The change is gated behind a `ROSLYNMCP_HOST_ANALYZERS` env var, ships opt-in for one v1.x release, then becomes the v2 default.

The plan has **no new MCP surface** and **no DTO changes**. Every existing diagnostic-touching tool (`project_diagnostics`, `compile_check`, `diagnostic_details`, `code_fix_preview`, `fix_all_preview`, `dead_code_detection`, `security_diagnostics`, `find_unused_symbols`) becomes more accurate without a contract bump.

---

## 2. Background

### 2.1 What the documentation says

> *"IDE and CA analyzers not loaded in MSBuildWorkspace — only SDK-implicit diagnostics active at runtime (AUDIT-21)."*  
> — `ai_docs/architecture.md:83`

> *"AUDIT-21 | IDE/CA analyzers vs SDK-implicit diagnostics in `MSBuildWorkspace` | Documented gap | `ai_docs/architecture.md` — fix only if product requires full analyzer load; likely separate spike."*  
> — `docs/parity-gap-implementation-plan.md:75`

### 2.2 What the code actually does today

The picture is more nuanced than "no IDE/CA analyzers." A current trace through diagnostics:

1. `DiagnosticService.GetDiagnosticsAsync` (`DiagnosticService.cs:35-150`) requests both raw compiler diagnostics and analyzer-bound diagnostics via `ICompilationCache`.
2. `CompilationCache.BuildCompilationWithAnalyzersAsync` (`CompilationCache.cs:95-119`) iterates `project.AnalyzerReferences`, drops `UnresolvedAnalyzerReference` entries, calls `.GetAnalyzers(project.Language)`, and binds them via `compilation.WithAnalyzers(...)`.
3. `project.AnalyzerReferences` contains only what MSBuild evaluation populated for the target project — i.e., analyzers a project's `.csproj` explicitly pulls in via `<PackageReference>` or `<Analyzer Include="…"/>`.
4. The IDE-side analyzers (everything in the `IDE0xxx` family) are **never** populated into `project.AnalyzerReferences` because they live behind IDE-only MEF composition (`Microsoft.CodeAnalysis.Features.MefHostServices`) that `MSBuildWorkspace` does not invoke.

The smoking gun is the existing guidance in `FixAllService.cs:53`:

> *"Restore analyzer packages (IDE/CA rules) or use organize_usings_preview / organize_usings_apply for unused usings (IDE0005). Use list_analyzers to see loaded diagnostic IDs."*

That guidance message exists because `fix_all_preview` cannot find a code fix provider for IDE0005 — even though the provider class **is loaded in-process** (via the `Microsoft.CodeAnalysis.CSharp.Features` package reference) and the code-fix scanning code in `CodeActionService.LoadCodeFixProviders` (`CodeActionService.cs:210-240`) does pick it up. The bottleneck is purely on the analyzer side: the diagnostic is never raised, so the fix is never offered.

### 2.3 Why this is the right "v2 stepping stone"

Of the four post-release roadmap items in `docs/roadmap.md` (HTTP/SSE host, editor-backed host, persistent indexing, MCP Registry publication), AUDIT-21 is the only one that closes a **correctness gap inside the existing v1 surface** rather than expanding scope. Every alternative roadmap item ships a new operational footprint that needs hardening from zero, and any future host (HTTP/SSE or VS-backed) would still ship the same hole at higher latency unless this is fixed first.

Concrete leverage:

- **Every diagnostic-touching tool becomes more accurate with no schema change.** No new tools, no DTO churn.
- **Unblocks `fix_all_preview` for the most common cases** (`IDE0005` unused usings, `IDE0044` make readonly, `IDE0090` simplify new) — currently the tool has to disclaim them.
- **Dissolves several backlog rows** (`compile-check-vs-analyzers-doc`, recurring "expected this rule to fire" notes in audit reports) without changing the tools they're attached to.
- **The reflection-scan template already exists in the codebase** (`CodeActionService.cs:198-272`) — this plan extends a known-good pattern instead of inventing one.

---

## 3. Goals and non-goals

### 3.1 Goals

| # | Goal |
|---|------|
| G1 | When `ROSLYNMCP_HOST_ANALYZERS=true`, all `DiagnosticAnalyzer` types shipped in `Microsoft.CodeAnalysis.Features.dll` and `Microsoft.CodeAnalysis.CSharp.Features.dll` (the host's pinned 5.3.0 versions) are bound to every C# project compilation that flows through `CompilationCache`. |
| G2 | A project's existing `AnalyzerReferences` are still honored. Host-injected analyzers are deduplicated against project-supplied analyzers by analyzer `System.Type` so a project that explicitly references `Microsoft.CodeAnalysis.NetAnalyzers` does not run CA rules twice. |
| G3 | `.editorconfig` severity overrides apply to host-injected analyzers exactly as they apply to project-supplied analyzers (no special-casing). |
| G4 | `code_fix_preview` and `fix_all_preview` resolve providers for newly surfaced IDE diagnostics without further changes — `CodeActionService.LoadCodeFixProviders` already loads them; the bottleneck has been the missing analyzer side. |
| G5 | The change ships behind a feature flag, defaulting to **off** for the bridge release and **on** for the v2.0 release. |
| G6 | Cold workspace performance regression is bounded and measured. New `PerformanceBaselineTests` budgets are documented and enforced. |
| G7 | A startup health check asserts the discovery scan returns at least N analyzers; if it returns zero (e.g., a future Roslyn refactor moves them), the host logs a Warning and degrades to "host analyzers disabled" rather than silently failing. |

### 3.2 Non-goals

| # | Non-goal |
|---|----------|
| NG1 | Loading non-Roslyn analyzer packs (StyleCop, Roslynator, SonarAnalyzer) at the host level. Those remain the project's responsibility via `<PackageReference>`. |
| NG2 | Implementing or imitating Visual Studio's full IDE diagnostic service. Live-buffer parity, on-the-fly analysis, and editor squiggles remain out of scope and are tracked separately in `docs/roadmap.md` § *Unsaved Buffer And Live Workspace Parity*. |
| NG3 | Auto-promoting `Hidden`-default IDE rules to `Warning`. The default severity filter in `DiagnosticService.cs:40` (`Warning`) still applies; users elevate via `.editorconfig` or by passing `severity: hint` to the tool. See § 6.3. |
| NG4 | Loading IDE refactoring providers beyond what `CodeActionService.LoadCodeRefactoringProviders` already does. That code path is unchanged. |
| NG5 | Resolving SDK version skew between the host's pinned Roslyn 5.3.0 and the user's solution SDK. Documented as a caveat (§ 11.2) but not corrected. |

---

## 4. Design overview

### 4.1 Component map

```
┌──────────────────────────────────────────────────────────────────┐
│ RoslynMcp.Host.Stdio                                             │
│   Program.cs                                                     │
│     └─ BindHostAnalyzerOptions()  ← new env var binding          │
│     └─ AddSingleton(HostAnalyzerOptions)                         │
└──────────────────────┬───────────────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────────────┐
│ RoslynMcp.Roslyn.ServiceCollectionExtensions.AddRoslynServices() │
│   AddSingleton<IHostAnalyzerProvider, HostAnalyzerProvider>()    │
└──────────────────────┬───────────────────────────────────────────┘
                       │
        ┌──────────────┴──────────────┐
        │                             │
┌───────▼─────────────┐    ┌──────────▼──────────────────┐
│ CompilationCache    │    │ (future: any service that   │
│   (modified)        │    │  needs host analyzers       │
│                     │    │  outside compilation cache) │
└─────────────────────┘    └─────────────────────────────┘
```

### 4.2 New types

| Type | Project | Purpose |
|------|---------|---------|
| `IHostAnalyzerProvider` | `RoslynMcp.Core/Services/` | Singleton contract returning `ImmutableArray<DiagnosticAnalyzer>` for a given language. |
| `HostAnalyzerProvider` | `RoslynMcp.Roslyn/Services/` | Reflection-scan implementation. Lazy-evaluated, cached for process lifetime, respects `HostAnalyzerOptions.Enabled`. |
| `HostAnalyzerOptions` | `RoslynMcp.Core/Models/` | Record with `Enabled : bool` and `MinExpectedAnalyzers : int` for the startup health check. |

`Core` already references `Microsoft.CodeAnalysis.Diagnostics` (see `ICompilationCache.cs:2` importing `DiagnosticAnalyzer`), so the new contract fits the established layering rule — Roslyn types may cross into `Core` when the contract genuinely wraps a Roslyn primitive.

### 4.3 Modified types

| File | Change |
|------|--------|
| `src/RoslynMcp.Roslyn/Services/CompilationCache.cs` | Inject `IHostAnalyzerProvider`. In `BuildCompilationWithAnalyzersAsync`, merge host analyzers with project-derived analyzers, deduplicating by analyzer `Type`. |
| `src/RoslynMcp.Roslyn/Services/CodeActionService.cs` | Load code-fix providers from `Microsoft.CodeAnalysis.Features` **in addition to** the existing C# Features scan, so language-agnostic IDE fixes are not missed. (Current code only scans `Microsoft.CodeAnalysis.CSharp.Features`.) |
| `src/RoslynMcp.Roslyn/Services/FixAllService.cs` | Update the guidance message at line 53 to drop the "Restore analyzer packages" hint when host analyzers are enabled. |
| `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs` | Register `IHostAnalyzerProvider` as a singleton. |
| `src/RoslynMcp.Host.Stdio/Program.cs` | Add `BindHostAnalyzerOptions()` (mirrors the existing `Bind*` helpers). Register the resulting options singleton. |
| `ai_docs/architecture.md` | Remove AUDIT-21 from § Known Gaps (or downgrade to "host-injected when enabled"). |
| `ai_docs/runtime.md` | Add `ROSLYNMCP_HOST_ANALYZERS` and `ROSLYNMCP_HOST_ANALYZER_MIN_COUNT` to the env var table. |
| `docs/parity-gap-implementation-plan.md` | Move AUDIT-21 from "Known architecture limitation" to release log. |
| Tool descriptions: `compile_check`, `project_diagnostics`, `fix_all_preview` | Note that host-injected IDE/CA analyzers are loaded by default (or by env var for the bridge release). |

---

## 5. Phased work breakdown

The plan ships as a single PR but is structured in phases that can be reviewed and tested independently. Each phase is buildable and produces a green test run before the next phase is added.

### Phase 1 — `IHostAnalyzerProvider` contract and implementation

**Files:**

- `src/RoslynMcp.Core/Services/IHostAnalyzerProvider.cs` (new)
- `src/RoslynMcp.Core/Models/HostAnalyzerOptions.cs` (new)
- `src/RoslynMcp.Roslyn/Services/HostAnalyzerProvider.cs` (new)

**Contract sketch:**

```csharp
// Core/Services/IHostAnalyzerProvider.cs
namespace RoslynMcp.Core.Services;

public interface IHostAnalyzerProvider
{
    /// <summary>
    /// Returns DiagnosticAnalyzer instances that the host injects into every compilation
    /// for the given language. Empty array when host analyzers are disabled.
    /// </summary>
    ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language);

    /// <summary>
    /// Diagnostic-only summary used by the startup health check and the list_analyzers tool.
    /// </summary>
    HostAnalyzerDiagnostics GetDiagnostics();
}

public sealed record HostAnalyzerDiagnostics(
    bool Enabled,
    int LoadedAnalyzerCount,
    IReadOnlyList<string> LoadedAssemblyNames,
    IReadOnlyList<string> Failures);
```

**Implementation sketch:**

```csharp
// Roslyn/Services/HostAnalyzerProvider.cs
public sealed class HostAnalyzerProvider : IHostAnalyzerProvider
{
    private static readonly string[] FeatureAssemblyNames =
    {
        "Microsoft.CodeAnalysis.Features",
        "Microsoft.CodeAnalysis.CSharp.Features",
    };

    private readonly HostAnalyzerOptions _options;
    private readonly ILogger<HostAnalyzerProvider> _logger;
    private readonly Lazy<ImmutableArray<DiagnosticAnalyzer>> _csharpAnalyzers;
    private readonly Lazy<HostAnalyzerDiagnostics> _diagnostics;

    public HostAnalyzerProvider(HostAnalyzerOptions options, ILogger<HostAnalyzerProvider> logger)
    {
        _options = options;
        _logger = logger;
        _csharpAnalyzers = new Lazy<ImmutableArray<DiagnosticAnalyzer>>(
            () => LoadAnalyzers(LanguageNames.CSharp),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _diagnostics = new Lazy<HostAnalyzerDiagnostics>(BuildDiagnostics);
    }

    public ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
    {
        if (!_options.Enabled) return ImmutableArray<DiagnosticAnalyzer>.Empty;
        return language == LanguageNames.CSharp
            ? _csharpAnalyzers.Value
            : ImmutableArray<DiagnosticAnalyzer>.Empty;
    }

    private ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers(string language)
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticAnalyzer>();
        var seenTypes = new HashSet<Type>();
        var failures = new List<string>();

        foreach (var assemblyName in FeatureAssemblyNames)
        {
            Assembly? assembly;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch (Exception ex)
            {
                failures.Add($"Load {assemblyName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException rtle)
            {
                types = rtle.Types.Where(t => t is not null).ToArray()!;
                failures.Add($"GetTypes {assemblyName}: {rtle.LoaderExceptions.Length} loader exceptions");
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type)) continue;

                // Filter to analyzers that declare support for the requested language.
                var attr = type.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false)
                    .OfType<DiagnosticAnalyzerAttribute>()
                    .FirstOrDefault();
                if (attr is null || !attr.Languages.Contains(language)) continue;
                if (!seenTypes.Add(type)) continue;

                try
                {
                    if (Activator.CreateInstance(type) is DiagnosticAnalyzer instance)
                    {
                        builder.Add(instance);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"Activate {type.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        var result = builder.ToImmutable();
        _logger.LogInformation(
            "HostAnalyzerProvider loaded {Count} {Language} analyzers from {Assemblies}",
            result.Length, language, string.Join(", ", FeatureAssemblyNames));
        if (failures.Count > 0)
        {
            _logger.LogWarning(
                "HostAnalyzerProvider encountered {FailureCount} discovery failures: {Failures}",
                failures.Count, string.Join(" | ", failures));
        }

        if (result.Length < _options.MinExpectedAnalyzers)
        {
            _logger.LogWarning(
                "HostAnalyzerProvider returned {Count} analyzers but expected at least {Min}. " +
                "A pinned Roslyn version change may have moved analyzer types out of the IDE features assemblies.",
                result.Length, _options.MinExpectedAnalyzers);
        }

        return result;
    }

    private HostAnalyzerDiagnostics BuildDiagnostics() { /* trivial composition of state */ }
}
```

**Validation criteria:**

- Returns ≥ 1 analyzer when invoked against the pinned Roslyn 5.3.0 packages on a clean build.
- Returns `ImmutableArray.Empty` when `_options.Enabled` is `false`.
- Survives `ReflectionTypeLoadException` partial loads without throwing.
- The single underlying scan runs at most once per process lifetime (verified by a unit test that calls `GetAnalyzers` from two threads concurrently).

### Phase 2 — DI registration and env var binding

**Files:**

- `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs`
- `src/RoslynMcp.Host.Stdio/Program.cs`

**Changes:**

1. In `ServiceCollectionExtensions.AddRoslynServices` (between lines 21 and 22, alongside other singleton registrations), add:
   ```csharp
   services.AddSingleton<IHostAnalyzerProvider>(sp =>
       new HostAnalyzerProvider(
           sp.GetService<HostAnalyzerOptions>() ?? new HostAnalyzerOptions(),
           sp.GetRequiredService<ILogger<HostAnalyzerProvider>>()));
   ```
2. In `Program.cs`, add `BindHostAnalyzerOptions()` mirroring the existing helpers (`Program.cs:80-167`):
   ```csharp
   static HostAnalyzerOptions BindHostAnalyzerOptions()
   {
       var opts = new HostAnalyzerOptions();
       var rawEnabled = Environment.GetEnvironmentVariable("ROSLYNMCP_HOST_ANALYZERS");
       if (bool.TryParse(rawEnabled, out var enabled))
           opts = opts with { Enabled = enabled };
       if (int.TryParse(Environment.GetEnvironmentVariable("ROSLYNMCP_HOST_ANALYZER_MIN_COUNT"), out var min) && min >= 0)
           opts = opts with { MinExpectedAnalyzers = min };
       return opts;
   }
   ```
3. Register the bound options at `Program.cs:23-28`:
   ```csharp
   builder.Services.AddSingleton(BindHostAnalyzerOptions());
   ```
4. **Default value:** ship `Enabled = false` for the bridge release. Flip to `Enabled = true` in the v2.0 commit.

### Phase 3 — `CompilationCache` injection

**File:** `src/RoslynMcp.Roslyn/Services/CompilationCache.cs`

**Changes:**

1. Add `IHostAnalyzerProvider` constructor parameter; store as field. The DI registration is unchanged (constructor injection via `AddSingleton<ICompilationCache, CompilationCache>` already resolves dependencies).
2. Modify `BuildCompilationWithAnalyzersAsync` (`CompilationCache.cs:95-119`) to merge host-injected analyzers with project analyzers:
   ```csharp
   private async Task<CompilationWithAnalyzers?> BuildCompilationWithAnalyzersAsync(
       string workspaceId, Project project, CancellationToken ct)
   {
       var compilation = await GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
       if (compilation is null) return null;

       // FLAG-A: skip UnresolvedAnalyzerReference entries (existing behavior).
       var projectAnalyzers = project.AnalyzerReferences
           .Where(reference => reference is not UnresolvedAnalyzerReference)
           .SelectMany(reference => reference.GetAnalyzers(project.Language))
           .ToImmutableArray();

       // AUDIT-21: merge host-injected analyzers, deduplicating by concrete Type so a project
       // that already references the same analyzer pack does not double-run rules.
       var hostAnalyzers = _hostAnalyzerProvider.GetAnalyzers(project.Language);
       var merged = projectAnalyzers;
       if (hostAnalyzers.Length > 0)
       {
           var projectTypes = projectAnalyzers.Select(a => a.GetType()).ToHashSet();
           var additions = hostAnalyzers.Where(a => projectTypes.Add(a.GetType())).ToImmutableArray();
           merged = projectAnalyzers.AddRange(additions);
       }

       if (merged.Length == 0) return null;

       return compilation.WithAnalyzers(
           merged,
           new CompilationWithAnalyzersOptions(
               options: project.AnalyzerOptions,
               onAnalyzerException: null,
               concurrentAnalysis: true,
               logAnalyzerExecutionTime: false,
               reportSuppressedDiagnostics: false));
   }
   ```

**Why dedup by `Type` and not by diagnostic ID:** two distinct analyzer classes can supply the same `DiagnosticDescriptor.Id`. Dedup by ID would silently drop one of them. Type-level dedup matches what `compilation.WithAnalyzers` would dedupe internally and avoids surprising users who explicitly pin a different analyzer pack.

**Why merge into the project list and not pre-bind:** `compilation.WithAnalyzers` requires every analyzer to be visible at bind time. Building a separate `CompilationWithAnalyzers` for host-injected diagnostics would double the analysis cost and prevent severity overrides in `.editorconfig` from taking effect (because each pass uses its own `AnalyzerOptions`). A single bind is correct and matches how Visual Studio composes its analyzer set.

### Phase 4 — `CodeActionService` parity

**File:** `src/RoslynMcp.Roslyn/Services/CodeActionService.cs`

The existing reflection scan at `CodeActionService.cs:198-272` only loads providers from `Microsoft.CodeAnalysis.CSharp.Features`. Some IDE code-fix providers (notably the language-agnostic ones in `Microsoft.CodeAnalysis.Features.dll`) are not picked up.

**Changes:**

1. Replace `LoadCSharpFeaturesAssembly()` with a multi-assembly variant:
   ```csharp
   private static IReadOnlyList<Assembly> LoadFeatureAssemblies()
   {
       var names = new[] { "Microsoft.CodeAnalysis.Features", "Microsoft.CodeAnalysis.CSharp.Features" };
       var loaded = new List<Assembly>(names.Length);
       foreach (var name in names)
       {
           try { loaded.Add(Assembly.Load(name)); }
           catch { /* logged at the call site */ }
       }
       return loaded;
   }
   ```
2. Update `LoadCodeFixProviders` and `LoadCodeRefactoringProviders` to flatten across assemblies, dedup by `Type`, and accumulate the same way as `HostAnalyzerProvider.LoadAnalyzers`. Reuse a private helper to avoid copy-paste between the two methods.
3. Filter code-fix providers by `[ExportCodeFixProvider]` language attribute (analogous to the analyzer filter) so non-C# providers are not activated against C# documents.

This phase is technically independent of the analyzer change but ships in the same PR — it removes the second half of the FixAll guidance message and is a one-line conceptual extension of work that already exists.

### Phase 5 — Guidance message and FixAllService cleanup

**File:** `src/RoslynMcp.Roslyn/Services/FixAllService.cs:53`

**Change:** when host analyzers are enabled, drop the "Restore analyzer packages (IDE/CA rules)" portion. Inject `IHostAnalyzerProvider` into `FixAllService`, check `GetDiagnostics().Enabled`, and produce one of two messages:

- Enabled: *"No code fix provider is loaded for diagnostic '{diagnosticId}'. Use list_analyzers to see loaded diagnostic IDs."*
- Disabled: *(unchanged current message)*

Same treatment for any other guidance strings that disclaim missing IDE rules — grep for "IDE/CA" and "Restore analyzer" and audit them.

### Phase 6 — Startup health check

**File:** `src/RoslynMcp.Host.Stdio/Program.cs:52-63`

Extend the existing startup health check (which currently logs an Information event when zero workspaces are loaded) to also resolve `IHostAnalyzerProvider`, call `GetDiagnostics()`, and emit a structured event:

- `Information` when `Enabled=true` and `LoadedAnalyzerCount >= MinExpectedAnalyzers`.
- `Warning` when `Enabled=true` and `LoadedAnalyzerCount < MinExpectedAnalyzers` (likely a pinned-version regression).
- `Information` (one-line) when `Enabled=false` so users can audit-trace whether the flag is on.

This keeps the failure mode loud — a future Roslyn package version that moves analyzer types will not silently produce zero diagnostics.

---

## 6. Testing strategy

### 6.1 New test fixture

Add a deliberately diagnostic-rich file to the existing `samples/SampleSolution/SampleLib` project (rather than a new project) so the existing `SharedWorkspaceTestBase` and `PerformanceBaselineTests` infrastructure picks it up automatically.

**File:** `samples/SampleSolution/SampleLib/HostAnalyzerProbe.cs` (new)

```csharp
// Deliberate triggers for IDE/CA analyzers exercised by HostAnalyzerProviderTests.
// Each line below is intentional — do not "fix" the warnings.
using System;                       // IDE0005 — using is unused once we drop the reference below
using System.Collections.Generic;   // IDE0005 — unused

namespace SampleLib;

#pragma warning disable CA1822 // intentionally non-static probe instance method
internal sealed class HostAnalyzerProbe
{
    // IDE0044 candidate: field could be made readonly
    private int _counter = 0;

    public int BumpAndReturn()
    {
        _counter++;
        return _counter;
    }
}
#pragma warning restore CA1822
```

The file is opted in for the host-analyzer flow but does not break the existing `SampleLib` build under `TreatWarningsAsErrors=false` (`SampleLib.csproj:6`).

**Important:** the existing `DiagnosticsProbe.cs` (`samples/SampleSolution/SampleLib/DiagnosticsProbe.cs`) is referenced by name in `DiagnosticServiceFilterTotalsTests` and `DiagnosticFixIntegrationTests`. **Do not remove or rename it** — append to the sample, do not replace.

### 6.2 New test class

**File:** `tests/RoslynMcp.Tests/HostAnalyzerProviderTests.cs` (new)

Test cases (each a `[TestMethod]`):

1. **`Provider_Loads_NonZero_Analyzers_When_Enabled`** — construct `HostAnalyzerProvider` with `Enabled=true`, assert `GetAnalyzers(LanguageNames.CSharp).Length > 0`.
2. **`Provider_Returns_Empty_When_Disabled`** — construct with `Enabled=false`, assert empty array.
3. **`Provider_Reflection_Scan_Is_Cached`** — call `GetAnalyzers` twice from two threads via `Parallel.For`, assert the underlying assembly load happened once (verifiable via a counting test logger or by asserting reference equality on the returned `ImmutableArray`).
4. **`Provider_Filters_By_Language`** — assert that calling `GetAnalyzers("VisualBasic")` returns empty (we only ship C#).
5. **`Provider_Survives_ReflectionTypeLoadException`** — use a fault-injection assembly resolver if practical, or assert the production call returns successfully against the real packages.
6. **`Diagnostics_Health_Check_Returns_Loaded_Count`** — sanity check on `GetDiagnostics()`.

### 6.3 Integration tests (extend existing fixtures)

**File:** `tests/RoslynMcp.Tests/HostAnalyzerIntegrationTests.cs` (new, mirrors `DiagnosticFixIntegrationTests`)

Each test loads the shared sample workspace via `SharedWorkspaceTestBase`, passing `Enabled=true` for the host analyzer options. The test class is opt-in via env var or DI override so it does not destabilize the existing fixture-shared `DiagnosticFixIntegrationTests` while the feature flag defaults to off.

Test cases:

1. **`Project_Diagnostics_Surfaces_IDE0005_From_HostAnalyzerProbe`**  
   Assert that `DiagnosticService.GetDiagnosticsAsync` with `severityFilter: "Hint"` returns at least one IDE0005 hit located in `HostAnalyzerProbe.cs`. The default `Warning` filter intentionally hides this — the test confirms the rule **fires**, not that it ships visible.

2. **`Code_Fix_Preview_Resolves_Provider_For_IDE0005`**  
   Call `GetCodeActions` against the IDE0005 location; assert at least one action whose Kind is `"CodeFix"` is returned. This is the regression test for the FixAllService guidance message — it should now succeed where it previously emitted "Restore analyzer packages."

3. **`Fix_All_Preview_For_IDE0005_In_Document_Scope_Succeeds`**  
   Call `FixAllService.PreviewFixAllAsync` with `diagnosticId: "IDE0005", scope: "document"`. Assert the result contains real text edits, not the guidance fallback.

4. **`EditorConfig_Severity_Override_Suppresses_Host_Analyzer`**  
   Drop a `.editorconfig` next to the probe with `dotnet_diagnostic.IDE0005.severity = none`. Reload the workspace, assert `project_diagnostics` returns zero IDE0005 hits — proves the host-injected analyzer respects user `.editorconfig` overrides through the existing `AnalyzerOptions` plumbing.

5. **`Project_Reference_Of_NetAnalyzers_Does_Not_Duplicate_Hits`**  
   Add a temporary `<PackageReference>` to `Microsoft.CodeAnalysis.NetAnalyzers` in an isolated copy of the sample, run `project_diagnostics`, assert that any CA1822 (or other CA rule) appears at most once per source location. This is the dedup correctness test.

6. **`Disabled_Flag_Reverts_To_Pre_Audit21_Behavior`**  
   With `Enabled=false`, assert `Project_Diagnostics_Surfaces_IDE0005_From_HostAnalyzerProbe` would fail — i.e., the feature flag really controls the new behavior. (Implement by flipping the flag in the test fixture's DI override and re-running the assertion in inverted form.)

### 6.4 Performance baseline updates

**File:** `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs`

Add two new methods (matching the budget-style of existing tests):

```csharp
[TestMethod]
public async Task ProjectDiagnostics_With_HostAnalyzers_Completes_Within_Budget()
{
    var sw = Stopwatch.StartNew();
    var result = await DiagnosticService.GetDiagnosticsAsync(
        WorkspaceId, projectFilter: null, fileFilter: null, severityFilter: "Hint", CancellationToken.None);
    sw.Stop();

    Assert.IsTrue(sw.ElapsedMilliseconds < 30_000,
        $"Diagnostics with host analyzers took {sw.ElapsedMilliseconds}ms — expected < 30s");
}

[TestMethod]
public async Task CompilationCache_Cold_Bind_Within_Budget()
{
    // Warm cache by loading workspace, then close and re-load to force re-bind.
    var copiedSolutionPath = CreateSampleSolutionCopy();
    var copiedRoot = Path.GetDirectoryName(copiedSolutionPath)!;
    try
    {
        var sw = Stopwatch.StartNew();
        var status = await WorkspaceManager.LoadAsync(copiedSolutionPath, CancellationToken.None);
        await DiagnosticService.GetDiagnosticsAsync(
            status.WorkspaceId, null, null, "Hint", CancellationToken.None);
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds < 60_000,
            $"Cold workspace load + diagnostics took {sw.ElapsedMilliseconds}ms — expected < 60s");

        WorkspaceManager.Close(status.WorkspaceId);
    }
    finally { DeleteDirectoryIfExists(copiedRoot); }
}
```

The 60s cold-load budget is generous on purpose — it tracks the existing `WorkspaceLoad_Completes_Within_Budget` (30s) + a 30s allowance for the analyzer bind. Tighten after the first run with real numbers.

### 6.5 Tests that may need updates

| Test | Why |
|------|-----|
| `DiagnosticServiceFilterTotalsTests` | Hit counts on the shared sample workspace will change once host analyzers fire. Confirm the test asserts shape (errors + warnings + info breakdown) rather than absolute counts; if it asserts absolute counts, update expected values. |
| `DiagnosticFixIntegrationTests` | Same caveat. The existing tests already use specific diagnostic IDs (`CS8019`, `CS0414`) which are compiler diagnostics, so they should be unaffected — but verify on a green run. |
| `CompilationCacheTests` (if it exists) | Add a test for the new host-analyzer merge path. Search `tests/RoslynMcp.Tests/` first to confirm whether the cache has a dedicated test file. |
| `SurfaceCatalogTests` | No change expected — no new tools or DTOs ship. |

---

## 7. Performance plan

### 7.1 Expected impact

Loading and binding ~150–250 additional analyzers per project (rough estimate from the IDE features assemblies in Roslyn 5.3.0) is the dominant new cost. The cost concentrates in:

1. **One-time process cost:** the reflection scan in `HostAnalyzerProvider.LoadAnalyzers`. Bounded; runs at most once per language per process.
2. **Per-project cold bind:** `compilation.WithAnalyzers(merged, …)` builds a new `CompilationWithAnalyzers` instance. Memory bump roughly proportional to analyzer count.
3. **Per-project first analysis run:** `GetAnalyzerDiagnosticsAsync` walks every newly bound analyzer. This is the main wall-clock cost — analyzers do significant work on first invocation, especially semantic ones.

### 7.2 What is already amortized

The existing `CompilationCache._analyzerBound` dictionary (`CompilationCache.cs:32`) memoizes `CompilationWithAnalyzers` per `(workspaceId, projectId, version)`. The cold cost is paid once per project per workspace version; subsequent calls reuse the same instance. This is the single most important reason the change is feasible — without the existing cache, every diagnostic call would re-bind analyzers.

Concurrent first-callers share the same in-flight task via the `Task<>`-storing pattern at `CompilationCache.cs:80-92`. Two parallel `project_diagnostics` calls do not double-bind.

### 7.3 Profiling plan before merging

1. Run `PerformanceBaselineTests` against the sample workspace before and after the change. Record both numbers in the PR.
2. Run a manual cold-load measurement against a representative external solution (the user's largest C# repo if available; the `docs/large-solution-profiling-baseline.md` template applies).
3. If P95 cold-load on the external solution exceeds 2× the pre-change baseline, do not merge — investigate whether a subset of analyzers can be excluded by name (e.g., the most expensive `IDE` rules).

### 7.4 Performance escape hatches

If profiling shows the change is too expensive at the default level:

- **Severity gate at bind time.** Pass `analyzerExclusionFilter` to `WithAnalyzers` to skip analyzers whose default severity is `Hidden` *and* whose `.editorconfig` does not elevate them. This is invasive and Roslyn does not expose a clean API for it — defer unless needed.
- **Allow-list mode.** Add `ROSLYNMCP_HOST_ANALYZER_ALLOWLIST` taking a comma-separated list of analyzer type names. If set, only those types are loaded. Useful as a customer-specific tuning knob.
- **Per-project bypass.** If a specific project hits a hot loop, allow `.editorconfig` to set `roslyn_mcp_host_analyzers = false` at the file level.

None of these are needed for the initial ship. They are documented here so reviewers can weigh in on whether to scope them in.

---

## 8. Rollout plan

### 8.1 Three-step rollout

| Step | Release | `Enabled` default | What ships |
|------|---------|-------------------|------------|
| 1 | v1.x bridge release (e.g., v1.9.0) | `false` | Code change + opt-in flag + tests + docs. No user-visible behavior change unless they set `ROSLYNMCP_HOST_ANALYZERS=true`. |
| 2 | v1.x soak release (e.g., v1.9.1+) | `false` | Documentation pass: the env var is recommended in audit prompts and skill SKILL.md files for internal validation. Collect operational feedback. |
| 3 | v2.0.0 | `true` | Default flips. AUDIT-21 is closed. `compile_check`, `project_diagnostics`, and `fix_all_preview` tool descriptions are rewritten to assume host analyzers are present. |

### 8.2 Communication

- `CHANGELOG.md` entry for each release describing the flag, default, and migration story.
- `docs/release-policy.md` does not need to change — this is a behavior expansion of an existing tool family, not a contract change.
- `README.md` env var section (auto-generated from `ai_docs/runtime.md`?) updated.
- A new entry in `docs/experimental-promotion-analysis.md` is **not** needed — no tool tier moves.

### 8.3 Rollback story

- Rollback is `ROSLYNMCP_HOST_ANALYZERS=false`, no redeploy required. The flag is read at startup so existing host processes need to be restarted, which is the standard MCP transport restart story.
- If a host process crashes during startup due to the reflection scan (extremely unlikely; the scan catches all exceptions), the env var lets users disable it without rolling back the binary.
- Code rollback is `git revert` of the merge commit. The change is contained to one PR by design.

---

## 9. Documentation updates

| File | Change |
|------|--------|
| `ai_docs/architecture.md` § Known Gaps | Either remove the AUDIT-21 line or rewrite as: *"IDE/CA analyzers are host-injected by default (v2.0+) via `IHostAnalyzerProvider`. Set `ROSLYNMCP_HOST_ANALYZERS=false` to revert to MSBuildWorkspace-only analyzer references."* |
| `ai_docs/runtime.md` § Environment variables | Add two rows: `ROSLYNMCP_HOST_ANALYZERS` and `ROSLYNMCP_HOST_ANALYZER_MIN_COUNT`. |
| `docs/parity-gap-implementation-plan.md` § Known architecture limitation | Remove the AUDIT-21 row (or move to a "Closed" subsection if one is added). |
| `ai_docs/backlog.md` | Remove the `compile-check-vs-analyzers-doc` row (it dissolves once host analyzers fire). Sync per the **Agent contract** at the top of that file. |
| Tool descriptions in `src/RoslynMcp.Host.Stdio/Tools/` | Update `compile_check`, `project_diagnostics`, and `fix_all_preview` descriptions to mention host-injected analyzers and the env var. Search for the existing string "analyzer-inclusive" in `compile_check`'s description and rewrite around it. |
| `src/RoslynMcp.Roslyn/Services/FixAllService.cs:53` | Remove the "Restore analyzer packages (IDE/CA rules)" hint when `Enabled=true`. |
| `CHANGELOG.md` | Two entries (one per rollout step). |
| Skills referencing diagnostics: `skills/explain-error/SKILL.md`, `skills/security/SKILL.md`, `skills/analyze/SKILL.md` | Audit for outdated language about "missing IDE rules" or workarounds. |
| `docs/product-contract.md` § Stable Surface | No change. The change is an accuracy improvement to existing stable tools. |

---

## 10. Risks and mitigations

| # | Risk | Severity | Mitigation |
|---|------|----------|-----------|
| R1 | **Reflection against unsanctioned API.** Roslyn's IDE features assemblies have no public contract for "list all `DiagnosticAnalyzer` types." A future Roslyn refactor could move types to a different assembly or change their attribute conventions. | Medium | Pin Roslyn version in `Directory.Packages.props` (already done — 5.3.0). Startup health check (§ 5 Phase 6) fails loud when discovery returns zero. CI test asserts a minimum analyzer count. |
| R2 | **SDK version skew.** Host pins Roslyn 5.3.0; user's solution may target a different SDK with different IDE rule behavior. | Medium | Document as a one-line caveat in tool descriptions. No mitigation in code — IDEs face the same problem and accept it. |
| R3 | **Cold-load performance regression.** First `project_diagnostics` call per project becomes substantially slower. | Medium | Existing `CompilationCache` already amortizes the cost. New `PerformanceBaselineTests` budget catches regressions. Profile against an external solution before merging. |
| R4 | **Duplicate analyzer execution.** A project that already references `Microsoft.CodeAnalysis.NetAnalyzers` could end up running CA rules twice. | Low | Type-level dedup in `CompilationCache.BuildCompilationWithAnalyzersAsync`. Test case in § 6.3.5. |
| R5 | **Hidden-default IDE rules flood `severity: hint` queries.** Users running `project_diagnostics` with `severity: hint` will see hundreds of new entries, possibly hitting the result limit. | Low | Document in tool description. Default severity filter remains `Warning`; users opt in to Hint. No change to the limit logic. |
| R6 | **Code-fix providers double-loaded by `CodeActionService`.** Phase 4 adds a second feature assembly to the existing scan. | Low | Type-level dedup applied symmetrically in `LoadCodeFixProviders`. |
| R7 | **Test fixture diagnostic counts shift.** Existing tests that assert absolute warning counts on the shared sample workspace may break. | Low | § 6.5 calls out the at-risk tests. Migration is a one-time fix. |
| R8 | **IDE analyzer initialization throws on construction.** Some IDE analyzers may require services that `MSBuildWorkspace` doesn't provide. | Medium | `Activator.CreateInstance` is wrapped in try/catch in `LoadAnalyzers`; failures are logged but the scan continues. Worst case: a subset of analyzers is loaded. The startup health check exposes the count, so a sudden drop is visible. |
| R9 | **Layer-boundary regression.** New `IHostAnalyzerProvider` interface in `Core` imports `DiagnosticAnalyzer`. | Low | Already established by `ICompilationCache.cs:2`. Architecture rule is "no raw Roslyn types in DTO contracts," which this respects — `DiagnosticAnalyzer` is a service primitive, not a DTO. |
| R10 | **Reflection scan on a trimmed/AOT host.** A future Native AOT publish path would fail because `Activator.CreateInstance(Type)` is trim-unsafe. | Low | Not a current scenario. `RoslynMcp.Host.Stdio` is published as a self-contained .NET app, not Native AOT. Document as a known incompatibility if AOT becomes a target. |

---

## 11. Open questions for review

These need decisions before implementation begins. Each has a **Recommendation** so the review can either accept or override.

**Q1. Default value of `ROSLYNMCP_HOST_ANALYZERS` for the bridge release.**  
Should the v1.x bridge release default to `Enabled=false` (opt-in) or `Enabled=true` (opt-out)?  
**Recommendation:** `false` for the bridge release. Flip in v2.0. This minimizes the blast radius of a behavior change that could affect every diagnostic-touching tool, and gives one release cycle to surface unknown unknowns.

**Q2. `MinExpectedAnalyzers` startup threshold.**  
What count should the startup health check use as the floor before logging a Warning?  
**Recommendation:** Set the initial value to `50` based on the empirical count from a one-off run against Roslyn 5.3.0 — well below the expected count (~150-250) but high enough to detect a catastrophic regression. Bake the actual measured number into the default after the first PR run.

**Q3. Should `list_analyzers` surface host-injected analyzers separately?**  
The existing `list_analyzers` tool (`AnalyzerInfoService.cs`) groups analyzers by source assembly. Host-injected analyzers will appear as `Microsoft.CodeAnalysis.Features` and `Microsoft.CodeAnalysis.CSharp.Features` entries that look indistinguishable from project references.  
**Recommendation:** Add a `Source: "Host" | "Project"` field to `AnalyzerInfoDto`. This is the only DTO change in the entire plan and is purely additive (existing clients ignore unknown fields). If the reviewer prefers zero DTO churn, drop this and rely on the assembly name as a soft signal.

**Q4. Should we elevate Hidden-default IDE rules?**  
Many `IDE0xxx` rules ship with `DefaultSeverity = Hidden`, meaning they fire but are filtered out by the default `severityFilter: "Warning"` in `DiagnosticService.cs:40`. After this change, users running with the default filter will see **no new diagnostics** unless they explicitly query `severity: hint` or set `EnforceCodeStyleInBuild=true` in their `.csproj`.  
**Recommendation:** Do **not** elevate. Match `dotnet build` semantics so users get consistent results across the host and the CLI. Document the gotcha in the tool description and make it a noted talking point in the release notes.

**Q5. Should this PR also load `SecurityCodeScan.VS2019` analyzers?**  
That package is referenced in `Directory.Packages.props:26` but only consumed by `SecurityDiagnosticService` today.  
**Recommendation:** Out of scope. Keep this PR focused on Roslyn IDE/CA analyzers only. The `SecurityCodeScan` integration follows a different code path and a separate review.

**Q6. Where should `HostAnalyzerOptions` live?**  
Two options:
- `RoslynMcp.Core/Models/HostAnalyzerOptions.cs` — alongside `WorkspaceManagerOptions`, `ValidationServiceOptions`, etc.
- `RoslynMcp.Roslyn/Services/HostAnalyzerOptions.cs` — colocated with the implementation.  
**Recommendation:** `Core/Models/`. The existing pattern in `Program.cs:80-167` is that all options records the host binds live in `Core` so the host can reference them without depending on `Roslyn` internals.

**Q7. Should `code_fix_preview` and `fix_all_preview` get an explicit `includeHostFixes` parameter?**  
Allows callers to opt out at the tool level rather than the env var level.  
**Recommendation:** **No.** The env var is sufficient. Adding a tool-level parameter expands the schema surface unnecessarily and complicates the documentation. Revisit if customers ask.

---

## 12. Acceptance criteria

The PR is mergeable when **all** of the following are true:

- [ ] All Phase 1–6 changes are in place and code-reviewed.
- [ ] `./eng/verify-release.ps1 -Configuration Release` is green.
- [ ] `./eng/verify-ai-docs.ps1` is green.
- [ ] `HostAnalyzerProviderTests` (§ 6.2) — six methods all pass.
- [ ] `HostAnalyzerIntegrationTests` (§ 6.3) — all six methods pass with `Enabled=true`.
- [ ] Existing `DiagnosticServiceFilterTotalsTests` and `DiagnosticFixIntegrationTests` still pass with the flag default (`Enabled=false`).
- [ ] New `PerformanceBaselineTests` budgets pass on a clean run; numbers recorded in the PR description.
- [ ] Cold-load measurement against an external 50+ project solution is documented in the PR. If P95 exceeds 2× the pre-change baseline, the PR is held for follow-up profiling.
- [ ] `ai_docs/architecture.md`, `ai_docs/runtime.md`, `docs/parity-gap-implementation-plan.md`, and the affected tool descriptions are updated in the same PR.
- [ ] `ai_docs/backlog.md` is synced (the `compile-check-vs-analyzers-doc` row is removed; this plan file is removed or moved to an `archive/` location only after the v2.0 default flip).
- [ ] `CHANGELOG.md` entry written under the bridge release version.
- [ ] Startup health check observed manually against a sample workspace; log lines look reasonable.

---

## 13. Backlog sync (workflow contract)

Per `ai_docs/workflow.md` § Backlog closure and `ai_docs/backlog.md` § Agent contract, this implementation plan ends with the required final todo when the work is executed:

- **`backlog: sync ai_docs/backlog.md`** — remove `compile-check-vs-analyzers-doc` (closed by Phase 5 + § 9 doc updates). Confirm no other backlog row depends on AUDIT-21 remaining open. The `revert-last-apply-disk-consistency` and `test-run-failure-envelope` rows are unaffected.

The plan file itself (`ai_docs/audit_21_implementaion_plan.md`) should be deleted or moved to `ai_docs/archive/` once the v2.0 default flip ships, per the archive policy in `ai_docs/archive/README.md`.

---

## 14. References

| File | Role |
|------|------|
| `ai_docs/architecture.md:81-83` | Source of the AUDIT-21 callout |
| `docs/parity-gap-implementation-plan.md:71-76` | Roadmap-level scoping ("likely separate spike") |
| `docs/roadmap.md:42-69` | Large-solution performance strategy that bounds the perf risk |
| `src/RoslynMcp.Roslyn/Services/CompilationCache.cs:95-119` | Primary modification site (Phase 3) |
| `src/RoslynMcp.Roslyn/Services/CodeActionService.cs:198-272` | Reflection-scan template that this plan reuses (Phase 1) and extends (Phase 4) |
| `src/RoslynMcp.Roslyn/Services/DiagnosticService.cs:35-150` | Consumer of the analyzer-bound compilation; no direct change but heavily affected |
| `src/RoslynMcp.Roslyn/Services/FixAllService.cs:48-55` | Guidance message that gets cleaned up in Phase 5 |
| `src/RoslynMcp.Core/Services/ICompilationCache.cs` | Layer precedent for Roslyn types in `Core` |
| `src/RoslynMcp.Roslyn/ServiceCollectionExtensions.cs:18-93` | DI registration site (Phase 2) |
| `src/RoslynMcp.Host.Stdio/Program.cs:80-167` | Env var binding pattern (Phase 2) |
| `tests/RoslynMcp.Tests/PerformanceBaselineTests.cs` | Budget test pattern (§ 6.4) |
| `tests/RoslynMcp.Tests/DiagnosticFixIntegrationTests.cs` | Integration test pattern (§ 6.3) |
| `samples/SampleSolution/SampleLib/DiagnosticsProbe.cs` | Existing fixture that the new probe file (`HostAnalyzerProbe.cs`) sits alongside |
| `Directory.Packages.props:7-11` | Pinned Roslyn version (5.3.0) |
