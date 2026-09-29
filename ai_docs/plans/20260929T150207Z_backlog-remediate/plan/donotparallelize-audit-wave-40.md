| Field | Content |
|---|---|
| Route | direct |
| Diagnosis | `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs:18` and `WorkspaceValidationTimeoutTests.cs:29` retain class-level serialization. `tests/RoslynMcp.Tests/AssemblyInfo.cs:3-6` still attributes opt-outs to TestBase mutable statics; current ownership must be rechecked after waves 01–39. |
| Approach | For each of the two classes, run bounded repeated concurrent evidence before and after removing `[DoNotParallelize]`; retain only an opt-out backed by a source-adjacent comment naming a process-global or shared-workspace dependency. Update `AssemblyInfo.cs` to describe the surviving ownership model and `CI_POLICY.md` with repeated serialized-tail TRX evidence. Do not weaken tests or add sleeps. |
| Scope | Production 0. Tests 3: `tests/RoslynMcp.Tests/WorkspaceToolsIntegrationTests.cs`, `tests/RoslynMcp.Tests/WorkspaceValidationTimeoutTests.cs`, `tests/RoslynMcp.Tests/AssemblyInfo.cs`. Docs 2: `CI_POLICY.md`. No deletions. Fragment: `changelog.d/donotparallelize-audit-wave-40.md`. |
| Tool policy | edit-only |
| Estimated context cost | 30000 |
| Risks | Assess shared-workspace and process-global resources before removing either attribute; a pass in isolation is insufficient evidence for concurrent safety. Test-only fanout is zero production files. |
| Validation | Bounded before/after concurrent class runs with TRX timings, then `just ci`; compare serialized tail and record measured result. |
| Performance review | Measure before/after serialized-tail TRX; no production hot-path change. |
| CHANGELOG category | Changed |
| CHANGELOG entry (draft) | Reduce unnecessary test serialization after verifying workspace test isolation. |
| Backlog sync | Close rows: [donotparallelize-audit-wave-40]. Mark obsolete: []. |
