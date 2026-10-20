# linux-msbuild-load-drops-case-distinct-documents — Investigate Linux MSBuild workspace load surfacing only one of two case-distinct Compile documents

**row:** `linux-msbuild-load-drops-case-distinct-documents` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs`
- `tests/RoslynMcp.Tests/CaseDistinctCallsitePreviewTests.cs`

## Acceptance

- [ ] Reproduce on Linux: a project with CaseCaller.cs and casecaller.cs loaded through WorkspaceManager exposes both documents, or the root cause (MSBuild glob de-dup, fixture, loader) is documented with evidence and any real defect is fixed.
- [ ] A regression test pins the verified behavior on both operating systems.

## Evidence

- The disk-based callsite tests for the path-identity PR saw only CaseCaller.cs on the Linux CI leg and were replaced with in-memory workspaces. Cause unverified.
