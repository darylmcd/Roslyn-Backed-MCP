# text-solution-native-membership-coordinates — Interpret standard solution membership using native separators

**row:** `text-solution-native-membership-coordinates` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs:580`
- `src/RoslynMcp.Roslyn/Services/SolutionFileMutationPlanner.cs:633`
- `tests/RoslynMcp.Tests/SolutionFileMutationEscapingTests.cs`

## Acceptance

- [ ] Resolve standard solution project entries using the shared native item-path interpretation on Windows and Linux.
- [ ] Apply identical semantics to membership removal and private absolute coordinate relocation.
- [ ] Keep literal percent sequences literal; do not apply MSBuild item unescaping to solution paths.
- [ ] Use actual Linux standard-solution loading, staging and removal controls with backslash entries; preserve untouched members/configuration.

## Evidence

- Two new renderer consumers use ordinary Path operations on raw .sln backslashes; Linux resolves a different filename. Existing .slnx control does not cover them. Fix in this PR.
