# workspace-msbuild-global-properties-value-redaction — keep caller property values out of logs

**row:** `workspace-msbuild-global-properties-value-redaction` · **pri:** `Medium` · **size:** `M` · **deps:** `workspace-lifecycle-argument-refusals-public-message`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceSessionLoader.cs:56-62` — Information logging renders every caller-supplied MSBuild global-property value.
- `tests/RoslynMcp.Tests/TestInfrastructure/ListLogger.cs:12` — existing rendered-message collector; extend or use a focused collector to inspect structured state too.
- `tests/RoslynMcp.Tests/WorkspaceSessionLoaderFailureTests.cs` — existing loader test infrastructure.

## Acceptance

- [ ] Remove arbitrary global-property values from rendered messages and structured logging state at every matching logging site and level.
- [ ] Retain useful safe counts; do not move the same values to Debug or Trace or maintain a secret-name denylist.
- [ ] Preserve the actual properties passed to MSBuildWorkspace unchanged.
- [ ] Add synthetic sentinel regression coverage for rendered output and structured state, including a caller-defined property name outside conventional credential names.
- [ ] Probe the source for all instances of this same value-logging mechanism and fix them together.

## Evidence

- Verified 2026-10-09 against immutable base d9ae7d6c12535e0d2ed3f87c4d68a1c4a0af8643: the loader logs string.Join over key=value pairs at Information before opening the project.
- Defective construct: `string.Join(", ", globalProperties.Select(kv => $"{kv.Key}={kv.Value}"))` is the `{Properties}` argument to `logger.LogInformation`.
- Source search found one matching global-property value renderer. WorkspaceManager forwards properties but has no corresponding value-log site.
- Pre-existing security/logging mechanism is independent of the lifecycle public-argument classification patch. No actual credentials were used or printed.
- Scope: one production file and focused tests; do not add a new initiative to the active remediation plan.

Live 2026-10-15: forced prerequisite of restore-reviewed-graph. Captured global properties must reach fork loading, restore and TestRunner execution unchanged. Fix all exposed loader and GatedCommandExecutor argv/output/timeout disclosure mechanisms together using synthetic raw and escaped sentinel controls. Supersedes earlier one-file scope estimate; no real credentials used.
