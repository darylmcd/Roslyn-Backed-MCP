# Upgrade matrix

This document maps **upgrade axes** for Roslyn-Backed MCP: what moves together, where it is pinned, and what to run after a change. Values below reflect the repository as of **2026-08-24**; when you bump a row, refresh the “Current” cells in the same PR.

Related: [Release policy](release-policy.md) (product version and gates),
[SDK 2.x wire-compatibility decision](decisions/0003-sdk-2x-wire-compatibility.md),
[SDK 2.2 servicing decision](decisions/0006-modelcontextprotocol-2-2-servicing.md),
[SDK 2.3 servicing decision](decisions/0031-modelcontextprotocol-2-3-servicing.md), and
[CI policy](../CI_POLICY.md) (merge validation).

---

## 1. Toolchain and TFM

| Axis | Current | Where pinned | Move with | After bump |
|------|---------|--------------|-----------|------------|
| .NET SDK (minimum) | `11.0.100` | `global.json` (`sdk.version`, `rollForward`: `latestFeature`) | Supplies the built-in .NET analyzers (`EnableNETAnalyzers`); CI exact-floor lane | `sdk-floor` job plus `./eng/verify-release.ps1`; confirm CI `setup-dotnet` still appropriate |
| CI / publish SDK channel | `11.0.x` SDK, plus `10.0.x` for the `net10.0` runtime | `.github/workflows/ci.yml`, `.github/workflows/publish-nuget.yml`; GitHub default CodeQL setup | `global.json` policy (exact vs floating) | If you pin CI to an exact SDK, document it here and verify the repository CodeQL setting |
| Target framework | `net10.0` | `Directory.Build.props` (`TargetFramework`) | SDK that supports the TFM; extension packages in the `10.0.x` line | Full build + test |

The SDK floor follows the Roslyn API family: the repository's release analyzer is built against the centrally pinned Roslyn API and fails with `CS9057` on an SDK whose compiler is older. Each stable Roslyn version ships with one SDK band (5.9 with `10.0.4xx`, 5.12 with `11.0.100`), so Roslyn 5.12 requires the `11.0.100` floor. The target framework stays `net10.0`; see [ADR 0039](decisions/0039-dotnet-11-sdk-floor.md).

---

## 2. Machine-checked central package inventory

`eng/verify-upgrade-matrix.ps1` requires exactly one row for every central pin. Missing, duplicate, malformed, extra, and stale rows fail CI.

| Package id | Current | Where pinned | Coupling / routing |
|------------|---------|--------------|--------------------|
| `ModelContextProtocol` | `2.3.0` | `Directory.Packages.props` | Contract-sensitive; dedicated PR, release-note/ADR review, notices, and raw-wire matrices |
| `Microsoft.CodeAnalysis.CSharp` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.Analyzers` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.CSharp.Features` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.Features` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.CodeAnalysis.CSharp.Scripting` | `5.12.0` | `Directory.Packages.props` | Roslyn API family; move together |
| `Microsoft.Build.Locator` | `1.11.2` | `Directory.Packages.props` | Review with MSBuild/workspace-loading changes |
| `Microsoft.Extensions.Hosting` | `10.0.14` | `Directory.Packages.props` | Extensions family; routine servicing group |
| `Microsoft.Extensions.Http` | `10.0.14` | `Directory.Packages.props` | Extensions family; routine servicing group |
| `Microsoft.Extensions.Logging` | `10.0.14` | `Directory.Packages.props` | Extensions family; routine servicing group |
| `Microsoft.Extensions.Logging.Console` | `10.0.14` | `Directory.Packages.props` | Extensions family; routine servicing group |
| `Microsoft.Extensions.TimeProvider.Testing` | `10.11.0` | `Directory.Packages.props` | Test-only; routine servicing group |
| `DiffPlex` | `1.9.0` | `Directory.Packages.props` | Independent |
| `Microsoft.Build.Framework` | `18.7.1` | `Directory.Packages.props` | Microsoft.Build compile family; group all updates including majors |
| `Microsoft.Build` | `18.7.1` | `Directory.Packages.props` | Microsoft.Build compile family; group all updates including majors |
| `Microsoft.Build.Tasks.Core` | `18.7.1` | `Directory.Packages.props` | Microsoft.Build compile family; group all updates including majors |
| `Microsoft.Build.Utilities.Core` | `18.7.1` | `Directory.Packages.props` | Microsoft.Build compile family; group all updates including majors |
| `Microsoft.NET.StringTools` | `18.7.1` | `Directory.Packages.props` | Microsoft.Build compile family; MSBuild 18.x runtime dependency excluded for MSBuildLocator |
| `Microsoft.NET.Test.Sdk` | `18.11.0` | `Directory.Packages.props` | Test infrastructure; routine servicing group |
| `MSTest.TestAdapter` | `4.5.2` | `Directory.Packages.props` | MSTest family; move with framework including majors |
| `MSTest.TestFramework` | `4.5.2` | `Directory.Packages.props` | MSTest family; move with adapter including majors |
| `coverlet.collector` | `10.1.0` | `Directory.Packages.props` | Coverage-only |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.MSTest` | `1.1.2` | `Directory.Packages.props` | Analyzer test harness; review Roslyn ABI |
| `NuGet.Frameworks` | `6.3.4` | `Directory.Packages.props` | Direct test pin required by MSBuildLocator asset policy |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | `5.6.0` | `Directory.Packages.props` | Independently versioned analyzer |
| `Nito.AsyncEx` | `5.1.2` | `Directory.Packages.props` | Independent |
| `Microsoft.SourceLink.GitHub` | `10.0.403` | `Directory.Packages.props` | Routine SDK-wave servicing |
| `System.Security.Cryptography.Xml` | `10.0.14` | `Directory.Packages.props` | Security override; update central rationale and notices together |

**Samples:** `samples/GeneratedDocumentSolution/ConsumerLib.Generators` uses `VersionOverride="5.0.0"` for `Microsoft.CodeAnalysis.CSharp` intentionally; it is outside central-package parity and moves only when that sample scenario requires it.

---

## 3. Coordinated dependency families

- Keep the Roslyn API family on one version. Analyzer tests and production analysis share this workspace ABI.
- Keep the four `Microsoft.Build*` compile references and `Microsoft.NET.StringTools` on one version. They use `PrivateAssets="all"` and `ExcludeAssets="runtime"`; a major upgrade must promote any new runtime dependency to the same direct-exclusion policy and pass the exact SDK-floor lane. The pin is the lowest version the Roslyn API family requires, so the compiled API surface stays within the MSBuild that older installed SDKs load at run time.
- Keep the Extensions family in one servicing line.
- Keep MSTest adapter and framework paired.
- Route `ModelContextProtocol` outside generic Dependabot groups. Every SDK bump requires a dedicated review, an ADR disposition, refreshed notices and matrix evidence, and all supported raw-wire protocol eras.

---

## 4. MCP SDK wire contract

The repository adopted MCP SDK 2.1 directly from 1.4.1, then advanced to 2.2.0 and 2.3.0 through
dedicated servicing reviews. The evaluated official sequence is 2.0.0-preview.1, preview.2, preview.3, rc.1,
rc.2, 2.0.0, 2.1.0, 2.2.0, and 2.3.0; there is no SDK 2.0.1 or 3.x in that lineage. RoslynMcp product
2.x/3.x versions are independent. SDK 2.3 preserves both protocol eras: modern `2026-07-28`
requests use `server/discover` plus request-scoped metadata, while initialize-capable clients
negotiate a down-level revision. The wire contract is recorded in
[ADR 0003](decisions/0003-sdk-2x-wire-compatibility.md); the non-breaking 2.2 and 2.3 servicing
dispositions are recorded in
[ADR 0006](decisions/0006-modelcontextprotocol-2-2-servicing.md) and
[ADR 0031](decisions/0031-modelcontextprotocol-2-3-servicing.md).

Protocol logging is retired: the server advertises `logging: false`, emits no
`notifications/message`, and keeps operational output on stderr. Secret-safe structured unexpected-
failure diagnostics are opt-in through `ROSLYNMCP_OBSERVABILITY_SINK=stderr` and are independent of
the negotiated MCP protocol revision.

---

## 5. CI-only tools

ReportGenerator `5.4.7` is pinned in `.github/workflows/ci.yml`; it produces the informational HTML coverage summary and is independent of central NuGet versions.

---

## 6. Product version (ship line)

Not NuGet: the **application and plugin version** must match across seven files. See [Release policy — Where to bump the version string](release-policy.md#where-to-bump-the-version-string).

| Source of truth | Field |
|-----------------|--------|
| `Directory.Build.props` | `<Version>` (also drives assembly / `server_info`) |
| `manifest.json`, `.claude-plugin/plugin.json`, `.claude-plugin/marketplace.json`, `.claude-plugin/mcp.json`, `.claude-plugin/server.json`, `CHANGELOG.md` | Per release policy; the `dnx` package pin and both version fields in `server.json` move together |

Automated check: `eng/verify-version-drift.ps1` (invoked from `eng/verify-release.ps1`).

---

## 7. Quick decision guide

| You are changing | Minimum checklist |
|------------------|-------------------|
| `global.json` SDK | The SDK supplies the built-in .NET analyzers, so there is no analyzer package to adjust; update the exact `sdk-floor` lane; run `verify-release.ps1`. |
| Any `Microsoft.CodeAnalysis.*` (Roslyn API) version | Bump **all** rows in section 2 together; raise the SDK floor to the band that ships that compiler; run full tests; watch MSBuild workspace integration. |
| `Microsoft.Build.*` or `Microsoft.Build.Locator` | Keep all five compile-family pins equal; full `verify-release.ps1`; exact-floor lane; exercise solution load + `build_workspace` / `test_run` paths. |
| `ModelContextProtocol` | Review every official release since the pin; add an ADR disposition that preserves or explicitly supersedes the current wire decision; run raw-wire matrices for every supported protocol era; check tool/prompt/resource registration and schemas. |
| `Microsoft.Extensions.*` | Host startup, logging, and shutdown; no special Roslyn coupling. |
| Product version only | All seven version files + `eng/verify-version-drift.ps1`. |

---

## 8. Commands (sanity)

```powershell
# Full local gate (restore, build, test, publish, version drift)
./eng/verify-release.ps1 -Configuration Release

# Outdated NuGet packages (informational; does not replace coordinated bumps above)
dotnet list RoslynMcp.slnx package --outdated

# Vulnerable packages (same fail-closed verifier used by CI and `just vuln-audit`)
pwsh -NoProfile -File ./eng/verify-nuget-audit.ps1
```

When this matrix and the repo drift, update **this file** in the same change set as the version pins so the table stays trustworthy.
