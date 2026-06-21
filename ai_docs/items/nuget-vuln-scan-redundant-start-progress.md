# nuget-vuln-scan-redundant-start-progress — drop the redundant bare start-progress emission

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/SecurityTools.cs` (`ScanNuGetVulnerabilities` — `ProgressHelper.Report(progress, 0, 1)` immediately followed by `ReportStage(progress, 0, 1, "scanning-nuget")`)

## Acceptance

- [ ] `ScanNuGetVulnerabilities` opens with a single `ReportStage(...,"scanning-nuget")` start emission (no preceding bare `Report` at the same 0/1 coordinates), consistent with `ValidationTools`/`WorkspaceWarmTools` which open directly with `ReportStage`.
- [ ] Existing nuget-vuln-scan tests still pass.

## Evidence

PR #1008 added the `"scanning-nuget"` stage emission but left the pre-existing bare `Report(0,1)` immediately before it, double-emitting at Progress=0. Sibling stage-emitting tools open directly with `ReportStage`. Harmless (one extra advisory notification) but inconsistent. Source: 2026-06-21 backlog-sweep code-quality review of PR #1008 (severity: low).

## Context

One-line cleanup in the tool wrapper.
