# ADR 0027: Explicit validation scope and actionable test-run diagnostics

Status: Accepted for the next major release, 2026-10-15.

The validation service treated an explicit empty changed-file list as omitted scope and consulted the change tracker and Git. This contradicted the tool's empty-list contract. Symbol search also accepted page endpoints whose integer addition overflowed, and two test-run caller refusals lost their corrective guidance at the host boundary.

Preserve an explicit empty list as an empty discovery scope. Omitted scope still uses the tracker and reconciliation. Compilation remains workspace-wide. Reject symbol-search requests whose `offset + limit` exceeds `Int32.MaxValue` before running the search. Keep representable terminal pages valid.

Publish authored, path-free test-run corrections for a selected non-test project and a workspace without test projects. Preserve `InvalidOperation`, `InvalidOperationException`, schema guidance, and supported protocol framing. Never include submitted project names, workspace identifiers, or lower-layer messages in those corrections. Existing mixed-runner guidance likewise needs no workspace identifier. Every copied project-list remedy must specify `workspace_status` with `verbose=true`, because summary mode omits the project tree.

A supplied test filter that reports zero tests adds a safe warning identifying the filter parameter without echoing its value. Keep the actual process success, exit code, counts, and other warnings. This warning uses the existing optional warnings collection and does not imply a failed execution.

These scope, validation-bound, and message corrections are observable changes under the [release policy](../release-policy.md). Include the pending breaking fragment in the next major release; do not consume it in a minor or patch release. No version files change in this remediation.

Migration: omit `changedFilePaths` to use tracked scope; pass `[]` to request no changed files. Use representable pagination endpoints. Select a test project using `projectName`, or add a test SDK reference when the workspace has no tests. Treat category and schema guidance as the failure contract rather than parsing old prose. Inspect warnings and test counts before treating a filtered run as verification.

Verification requires old-behavior regressions, both supported raw-wire protocol eras, successful pagination/test controls, warning composition, and the full local and hosted gates.
