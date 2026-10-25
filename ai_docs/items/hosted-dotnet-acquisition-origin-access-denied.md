# hosted-dotnet-acquisition-origin-access-denied — Restore latest-SDK hosted qualification

**row:** `hosted-dotnet-acquisition-origin-access-denied` · **pri:** `High` · **size:** `S`

## Anchors

- `.github/workflows/ci.yml:142` — Pinned setup-dotnet action requests the latest 10.0.x SDK for every matrix leg.
- [PR1835 CI](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40223866909) — All six Windows/Linux matrix legs fail during SDK installation, before repository tests.
- [Current-main reproduction](https://github.com/darylmcd/Roslyn-Backed-MCP/actions/runs/40224224815) — Base fc5bb88676214a942daa727c7ca88ac8dd34b61c reproduces SDK/runtime HTTP409 during setup; exact-floor lane passes.

## Acceptance

- [ ] Resolve or verify resolution of upstream SDK acquisition failure while preserving latest-10.0.x and isolated exact-floor coverage. Do not pin an older SDK, weaken a gate or retry until green.
- [ ] Establish a concrete external-state or reviewed installer change before repeating qualification. Record original failed runs and why the new attempt is justified.
- [ ] Obtain every required hosted check on the unchanged reviewed metadata head or independently reviewed replacement; retain public-runner topology and SHA-pinned action policy.

## Evidence

- 2026-10-25: Installer selects SDK10.0.403/runtime10.0.14; dotnetorigin.blob.core.windows.net returns HTTP409: Public access is not permitted on this storage account.
- Direct HEAD probes reproduce origin409; official builds.dotnet.microsoft.com and dotnetcli.blob.core.windows.net SDK endpoints respond200. Availability alone does not authorize a new download implementation.
- Pinned setup-dotnet action exposes no SDK-feed override input. No repository workflow was modified; no failed test run was retried.
- External owner controls origin access and redirect routing. Product code cannot repair that service configuration; preserve draft PR1835 and its passing independent/scoped receipts while acquisition remains blocked.
