# sanctioned-roots-unconfigured-error-misattributed — Return an actionable error when no sanctioned roots are configured

**row:** `sanctioned-roots-unconfigured-error-misattributed` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ClientRootPathValidator.cs:153`

## Acceptance

- [ ] With ROSLYNMCP_SANCTIONED_ROOTS unset (fail-closed), workspace_load on a valid absolute .slnx returns a message naming ROSLYNMCP_SANCTIONED_ROOTS / ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN, not 'Parameter path is invalid … expected types'
- [ ] The message does not echo the requested path (redaction contract preserved)
- [ ] Regression test asserts the envelope message for the no-roots branch

## Evidence

- Raw harness, server without roots env: workspace_load{path:<repo>/.../SampleSolution.slnx} → InvalidArgument "Parameter 'path' is invalid. Check that all required parameters are provided and values match the expected types."; cause only on stderr ('Filesystem boundary is not configured'). Reproduces on 4.2.1 and HEAD e24a48a4. — see `ai_docs/audits/20260924-1305/report.md` (check C1) and `ai_docs/audits/20260924-1305/findings.json`

## Context

- HandleMissingConfiguration throws a plain ArgumentException whose message embeds the path, so ToolErrorHandler.BuildSafeArgumentMessage (:646) redacts it to the generic template. The configured-but-outside-root branch already uses the marked CreateSanctionedRootBoundaryRefusal.
- First-run hosts (a new MCP registration without the env var, Dockerfile, .cursor/.vscode registrations — see dockerfile-run-comment-and-sanctioned-roots-env, repo-local-mcp-registrations-decision) hit this on their very first call.
