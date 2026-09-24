| Field | Content |
|---|---|
| Route | `direct` |
| Diagnosis | `ClientRootPathValidator.HandleMissingConfiguration` (`src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:153`) throws a plain `ArgumentException` embedding the path, so `ToolErrorHandler.BuildSafeArgumentMessage` redacts it to the generic "Parameter 'path' is invalid" template. The outside-root branch already uses the marked `CreateSanctionedRootBoundaryRefusal`; confirms the row. |
| Approach | Row Acceptance verbatim: (1) With ROSLYNMCP_SANCTIONED_ROOTS unset (fail-closed), workspace_load on a valid absolute .slnx returns a message naming ROSLYNMCP_SANCTIONED_ROOTS / ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN, not 'Parameter path is invalid … expected types'; (2) The message does not echo the requested path (redaction contract preserved); (3) Regression test asserts the envelope message for the no-roots branch. Regression test first in `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`. |
| Scope | Production files (1): `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs`. Test files (1): `tests/RoslynMcp.Tests/ClientRootPathValidatorTests.cs`. No deletions. |
| Tool policy | `edit-only` |
| Estimated context cost | 25000 |
| Risks | Must keep the redaction contract (no path in the client message) — mirror how `SanctionedRootBoundaryRefusalMessage` is marked so ToolErrorHandler passes it through. Logger lines may keep the path (server-side only). Fanout probe: private static method, single file; 0 external ripple. |
| Validation | Targeted `test_run --filter "FullyQualifiedName~<TestClass>"` per edit; full addenda `ci_equivalent` (`just ci`) before PR. |
| Performance review | N/A — correctness fix, no hot-path changes. |
| CHANGELOG category | Fixed |
| CHANGELOG entry (draft) | `workspace_load` and other path-taking tools now return an actionable error naming `ROSLYNMCP_SANCTIONED_ROOTS` / `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` when no sanctioned roots are configured, instead of a generic invalid-parameter message. |
| Backlog sync | Close rows: [sanctioned-roots-unconfigured-error-misattributed]. Mark obsolete: []. |
