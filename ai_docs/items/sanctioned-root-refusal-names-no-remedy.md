# sanctioned-root-refusal-names-no-remedy — The sanctioned-root refusal gives the caller nothing to act on

**row:** `sanctioned-root-refusal-names-no-remedy` · **pri:** `Low` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`
- `src/RoslynMcp.Host.Stdio/Security/SecurityOptionsEnvironmentBinder.cs`
- `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`

## Acceptance

- [ ] DECISION first (public error text; this repo is in contract-care mode): what the refusal may disclose. Recommended: never the configured root paths (the redaction rows forbid it), but the remedy — the operator variable `ROSLYNMCP_SANCTIONED_ROOTS`, the `expandSanctionedRoots` + `ROSLYNMCP_ALLOW_ROOT_EXPANSION` route, and the tool or resource that reports whether a boundary is configured.
- [ ] The refusal carries that remedy in the message or a structured field; `ClientRootPathValidatorTests` pins the text and that no configured path appears in it.
- [ ] Changelog fragment and migration note per the contract-care rule if the public message or error shape changes.

## Evidence

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:177-178` — `SanctionedRootBoundaryRefusalMessage = "The requested path is outside the configured sanctioned-root boundary."`.
- Observed 2026-10-09 from a Claude session rooted in another directory: `workspace_load({path: "D:/Roslyn-Backed-MCP/RoslynMcp.slnx"})` → `InvalidArgument` with exactly that message; the caller had to read `Security/SecurityOptionsEnvironmentBinder.cs:11` to learn the variable name.
- Related, not the same: `root-boundary-argument-refusals-public-message` (classification of the refusal, BLOCKED on the error-category migration) and `client-root-widening-drive-root-parent`. Coordinate the wording with the first.

## Context

- regression_shape: a refusal that states the rule and not the way to satisfy it.
