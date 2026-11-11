# native-test-filter-public-projection — Redact native MTP filters on the public response

**row:** `native-test-filter-public-projection` · **pri:** `Defer` · **size:** `M` · **verified:** `52f2c418cf5ba982b2a1c0914dd3977655d93eb4`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/TestRunPublicProjection.cs:109-160`
- `src/RoslynMcp.Roslyn/Services/TestRunnerService.cs:446-469`

## Acceptance

- [ ] After approved major release admission, redact the native MTP treenode filter in public argv and wherever the child process echoes it. Prove both supported protocol eras and full/compact responses against hostile filter sentinels; preserve literal child-process argv and internal diagnostics. Record the ADR and migration.

## Evidence

- Base 52f2c418: TestRunnerService passes --treenode-filter; TestRunPublicProjection only treats --filter as sensitive. The native filter value passes through publicly projected argv and is absent from the echo redaction list. Source inspection only.

## Context

- Stable test_run callers can observe the projected argument value; removal changes released stable behavior. Review for the next operator-approved major, with migration and raw-wire coverage. Log output is separately tracked by executed-log-line-leaks-filter-and-paths.
