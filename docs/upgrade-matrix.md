# Upgrade matrix

This document maps **upgrade axes** for Roslyn-Backed MCP: what moves together, where it is pinned, and what to run after a change. Values below reflect the repository as of **2026-08-12**; when you bump a row, refresh the “Current” cells in the same PR.

Related: [Release policy](release-policy.md) (product version and gates), [CI policy](../CI_POLICY.md) (merge validation).

---

## 1. Toolchain and TFM

| Axis | Current | Where pinned | Move with | After bump |
|------|---------|--------------|-----------|------------|
| .NET SDK (minimum) | `10.0.100` | `global.json` (`sdk.version`, `rollForward`: `latestFeature`) | Same band as `Microsoft.CodeAnalysis.NetAnalyzers` when possible; CI `dotnet-version` | `./eng/verify-release.ps1`; confirm CI `setup-dotnet` still appropriate |
| CI / publish SDK channel | `10.0.x` | `.github/workflows/ci.yml`, `codeql.yml`, `publish-nuget.yml` | `global.json` policy (exact vs floating) | If you pin CI to an exact SDK, document it here |
| Target framework | `net10.0` | `Directory.Build.props` (`TargetFramework`) | SDK that supports the TFM; extension packages in the `10.0.x` line | Full build + test |

---

## 2. Roslyn API stack (NuGet compiler / workspaces)

These packages **must stay on the same `Microsoft.CodeAnalysis.*` version** for `RoslynMcp.Roslyn` and any project that references them without an override.

| Package id | Current | Where pinned |
|------------|---------|----------------|
| `Microsoft.CodeAnalysis.CSharp` | `5.6.0` | `Directory.Packages.props` |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | `5.6.0` | `Directory.Packages.props` |
| `Microsoft.CodeAnalysis.CSharp.Features` | `5.6.0` | `Directory.Packages.props` |
| `Microsoft.CodeAnalysis.Features` | `5.6.0` | `Directory.Packages.props` |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | `5.6.0` | `Directory.Packages.props` |
| `Microsoft.CodeAnalysis.CSharp.Scripting` | `5.6.0` | `Directory.Packages.props` |

| Coupled axis | Current | Notes |
|--------------|---------|--------|
| MSBuild (API packages) | `17.14.28` (`Microsoft.Build`, `Framework`, `Tasks.Core`, `Utilities.Core`) | Used with workspace loading; mismatch with the SDK’s MSBuild can cause subtle load errors—bump only with a reason and full test pass. |
| `Microsoft.Build.Locator` | `1.11.2` | Often updated when MSBuild/workspace loading behavior changes. |

**Samples:** `samples/GeneratedDocumentSolution/ConsumerLib.Generators` uses `VersionOverride="5.0.0"` for `Microsoft.CodeAnalysis.CSharp` intentionally; that row is **not** central-managed parity—update only when the sample scenario requires it.

---

## 3. Analyzers and diagnostics (build-time)

| Package id | Current | Where pinned | Move with | After bump |
|------------|---------|--------------|-----------|------------|
| `Microsoft.CodeAnalysis.NetAnalyzers` | `10.0.302` | `Directory.Packages.props` | Same **SDK feature band** as `global.json` when practical (e.g. `10.0.100` SDK ↔ `10.0.100` analyzers) | `dotnet build` / fix new CA warnings (`TreatWarningsAsErrors` is on) |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | `5.6.0` | `Directory.Packages.props` | Independent of Roslyn API `5.x` line; follow package release notes | Build + review `BannedSymbols.txt` |

---

## 4. Host, protocol, and shared libraries

| Package id | Current | Where pinned | Move with |
|------------|---------|--------------|-----------|
| `ModelContextProtocol` | `2.1.0` | `Directory.Packages.props` | MCP protocol expectations; read upstream breaking changes |
| `Microsoft.Extensions.Hosting` | `10.0.10` | `Directory.Packages.props` | Other `Microsoft.Extensions.*` in same line |
| `Microsoft.Extensions.Logging` | `10.0.10` | `Directory.Packages.props` | Same |
| `Microsoft.Extensions.Logging.Console` | `10.0.10` | `Directory.Packages.props` | Same |
| `Nito.AsyncEx` | `5.1.2` | `Directory.Packages.props` | Independent |
| `DiffPlex` | `1.9.0` | `Directory.Packages.props` | Independent |
| `Microsoft.SourceLink.GitHub` | `10.0.301` | `Directory.Packages.props` | Often aligned with .NET / SDK wave; not runtime-critical |

MCP SDK 2.1 serves both protocol eras by default: modern `2026-07-28` requests use `server/discover` plus per-request metadata, while initialize-capable clients negotiate a down-level revision. This stdio host temporarily retains deprecated Roots, Sampling, and Logging compatibility. Their bounded replacements are tracked by `mcp-roots-configured-validation-migration`, `mcp-roots-query-discovery-migration`, `mcp-sampling-mrtr-migration`, and `mcp-logging-stderr-otel-migration`.

---

## 5. Tests and CI-only tools

| Component | Current | Where pinned | Notes |
|-----------|---------|--------------|--------|
| `Microsoft.NET.Test.Sdk` | `18.8.1` | `Directory.Packages.props` | Bump with test adapter/framework when needed |
| `MSTest.TestAdapter` / `MSTest.TestFramework` | `4.3.3` | `Directory.Packages.props` | Keep adapter and framework in sync |
| `coverlet.collector` | `10.0.1` | `Directory.Packages.props` | Coverage collection |
| ReportGenerator (global tool) | `5.4.7` | `.github/workflows/ci.yml` | HTML coverage summary only; independent of NuGet central versions |

---

## 6. Product version (ship line)

Not NuGet: the **application and plugin version** must match across five files. See [Release policy — Where to bump the version string](release-policy.md#where-to-bump-the-version-string).

| Source of truth | Field |
|-----------------|--------|
| `Directory.Build.props` | `<Version>` (also drives assembly / `server_info`) |
| `manifest.json`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `CHANGELOG.md` | Per release policy |

Automated check: `eng/verify-version-drift.ps1` (invoked from `eng/verify-release.ps1`).

---

## 7. Quick decision guide

| You are changing | Minimum checklist |
|------------------|-------------------|
| `global.json` SDK | Adjust `Microsoft.CodeAnalysis.NetAnalyzers` to the matching band if Microsoft publishes one; run `verify-release.ps1`; align CI if you switch major/minor. |
| Any `Microsoft.CodeAnalysis.*` (Roslyn API) version | Bump **all** rows in section 2 together; run full tests; watch MSBuild workspace integration. |
| `Microsoft.Build.*` or `Microsoft.Build.Locator` | Full `verify-release.ps1`; exercise solution load + `build_workspace` / `test_run` paths. |
| `ModelContextProtocol` | Integration smoke with a real MCP client; check tool registration and catalog. |
| `Microsoft.Extensions.*` | Host startup, logging, and shutdown; no special Roslyn coupling. |
| Product version only | All five version files + `eng/verify-version-drift.ps1`. |

---

## 8. Commands (sanity)

```powershell
# Full local gate (restore, build, test, publish, version drift)
./eng/verify-release.ps1 -Configuration Release

# Outdated NuGet packages (informational; does not replace coordinated bumps above)
dotnet list RoslynMcp.slnx package --outdated

# Vulnerable packages (also run in CI)
dotnet package list --project RoslynMcp.slnx --vulnerable --include-transitive
```

When this matrix and the repo drift, update **this file** in the same change set as the version pins so the table stays trustworthy.
