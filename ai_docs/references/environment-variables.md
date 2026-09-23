# Environment Variables

<!-- purpose: Canonical table of ROSLYNMCP_* environment overrides read by the stdio host. -->

Optional overrides are read at startup from `src/RoslynMcp.Host.Stdio/Program.cs` (plus `WorkspaceForkApplyService` for the `ROSLYNMCP_FORK_*` rows). Numeric values must be positive integers unless noted.

| Variable | Affects | Default |
|----------|---------|---------|
| `ROSLYNMCP_MAX_WORKSPACES` | `WorkspaceManagerOptions.MaxConcurrentWorkspaces` | 16 |
| `ROSLYNMCP_MAX_SOURCE_GENERATED_DOCS` | `WorkspaceManagerOptions.MaxSourceGeneratedDocuments` | 500 |
| `ROSLYNMCP_BUILD_TIMEOUT_SECONDS` | `ValidationServiceOptions.BuildTimeout` | 5 minutes |
| `ROSLYNMCP_TEST_TIMEOUT_SECONDS` | `ValidationServiceOptions.TestTimeout` | 10 minutes |
| `ROSLYNMCP_VULN_SCAN_TIMEOUT_SECONDS` | `ValidationServiceOptions.VulnerabilityScanTimeout` | 5 minutes |
| `ROSLYNMCP_APPLY_REVERT_TIMEOUT_SECONDS` | `ValidationServiceOptions.ApplyRevertTimeout` | 30 seconds |
| `ROSLYNMCP_GIT_STATUS_TIMEOUT_SECONDS` | `ValidationServiceOptions.GitStatusTimeout` — bounds the `git status` subprocess at both `validate_recent_git_changes`'s scope-collection (on timeout, reports `overallStatus: git-status-unknown` instead of `clean`) and `validate_workspace`'s change-tracker reconcile fallback when `changedFilePaths` is omitted (on timeout, silently falls back to the unfiltered tracker list, no verdict change) | 10 seconds |
| `ROSLYNMCP_MAX_RELATED_FILES` | `ValidationServiceOptions.MaxRelatedFiles` | 25 |
| `ROSLYNMCP_REFERENCE_RESPONSE_MAX_BYTES` | Complete UTF-8 JSON byte ceiling for successful `find_references` reference pages; minimum 1024, invalid values use the default. Follow additive `nextOffset` while `hasMore=true`; terminal pages return null. An individual reference that cannot fit returns an error: retry with `summary=true` or raise the operator budget. | 32000 |
| `ROSLYNMCP_FAST_FAIL_FILE_LOCK` | Early terminate `dotnet test` on MSB3027/MSB3021 file-lock failures | `true` |
| `ROSLYNMCP_PREVIEW_MAX_ENTRIES` | `PreviewStoreOptions.MaxEntries` | 20 |
| `ROSLYNMCP_PREVIEW_TTL_MINUTES` | `PreviewStoreOptions.TtlMinutes` | 5 minutes |
| `ROSLYNMCP_PREVIEW_PERSIST_DIR` | Persist composite preview tokens for cross-process apply flows | unset |
| `ROSLYNMCP_RATE_LIMIT_MAX_REQUESTS` | `ExecutionGateOptions.RateLimitMaxRequests` | 120 |
| `ROSLYNMCP_RATE_LIMIT_WINDOW_SECONDS` | `ExecutionGateOptions.RateLimitWindow` | 60 |
| `ROSLYNMCP_REQUEST_TIMEOUT_SECONDS` | `ExecutionGateOptions.RequestTimeout` | 120 |
| `ROSLYNMCP_ON_STALE` | `ExecutionGateOptions.OnStale` (`auto-reload`, `warn`, `off`) | `auto-reload` |
| `ROSLYNMCP_TOOL_TIERS` | Registered MCP surface support tiers (`stable` or `stable,experimental`) across tools, prompts, and resources; experimental entries require the stable baseline; use `stable` for non-deferring clients | `stable,experimental` |
| `ROSLYNMCP_OBSERVABILITY_SINK` | Operator-side diagnostics: `disabled`, secret-safe structured unexpected failures on `stderr`, or the full enabled `ILogger` stream as bounded per-process JSON lines under the metadata root with `file`; independent of MCP protocol logging | `disabled` |
| `ROSLYNMCP_SANCTIONED_ROOTS` | `SecurityOptions.SanctionedRoots` — canonical path-validation and solution-discovery boundary; delimit with `Path.PathSeparator` (`;` Windows, `:` macOS/Linux) | empty (path access denied) |
| `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN` | `SecurityOptions.PathValidationFailOpen` | `false` |
| `ROSLYNMCP_ALLOW_ROOT_EXPANSION` | `SecurityOptions.AllowRootExpansion` — server opt-in required in addition to a request's `expandSanctionedRoots=true`; widens each configured root by one parent for sibling worktrees | `false` |
| `ROSLYNMCP_SCRIPT_MAX_CONCURRENT` | `ScriptingServiceOptions.MaxConcurrentEvaluations` | 4 |
| `ROSLYNMCP_SCRIPT_SLOT_WAIT_SECONDS` | `ScriptingServiceOptions.ConcurrencySlotAcquireTimeoutSeconds` | 5 seconds |
| `ROSLYNMCP_SCRIPT_MAX_ABANDONED` | `ScriptingServiceOptions.MaxAbandonedEvaluations` — fail-fast cap for worker processes the OS could not terminate | 8 |
| `ROSLYNMCP_SCRIPT_TIMEOUT_SECONDS` | `ScriptingServiceOptions.TimeoutSeconds` | 10 seconds |
| `ROSLYNMCP_SCRIPT_HEARTBEAT_MS` | `ScriptingServiceOptions.HeartbeatIntervalMs` | 2000 milliseconds |
| `ROSLYNMCP_SCRIPT_STUCK_WARNING_SECONDS` | `ScriptingServiceOptions.StuckWarningSeconds` | 5 seconds |
| `ROSLYNMCP_SCRIPT_WATCHDOG_GRACE_SECONDS` | `ScriptingServiceOptions.WatchdogGraceSeconds` — grace after cooperative cancellation before isolated-worker termination | 10 seconds |
| `ROSLYNMCP_RESTORE_RACE_WAIT_MS` | `WorkspaceManagerOptions.RestoreRaceWaitMs` — max wait for a concurrent out-of-process `dotnet restore` to settle before `workspace_load` snapshots; `0` disables the wait (non-negative integer) | 2000 milliseconds |
| `ROSLYNMCP_FORK_TTL_HOURS` | `WorkspaceForkApplyService` — age after which expired `.roslynmcp/forks/` directories are swept; `0` disables the sweep | 24 hours |
| `ROSLYNMCP_FORK_RESTORE_TIMEOUT_MINUTES` | `WorkspaceForkApplyService` — timeout for the restore step of `workspace_fork_apply` | 2 minutes |
| `ROSLYNMCP_FORK_DOTNET_PATH` | `WorkspaceForkApplyService` — `dotnet` executable used for fork restore | `dotnet` |

