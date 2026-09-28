# server-info-hook-checkstatus-unknown — key the server_info hook's unknown-update branch on checkStatus before the 4.3.0 cut

**row:** `server-info-hook-checkstatus-unknown` · **pri:** `High` · **size:** `S`

## Anchors

- `hooks/hooks.json`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs`

## Acceptance

- [ ] Land before the 4.3.0 cut. `hooks/hooks.json` is release-managed (`ai_docs/workflow.md` § Release-managed file guard), so the edit needs the operator's `.release-managed-edit-allowed` sentinel or an operator-made edit. PR #1663 could not create the sentinel.
- [ ] The `server_info` PostToolUse prompt keys "unknown" on `update.checkStatus` rather than a null `updateAvailable`. It keeps the `update.updateAvailable` token that `Shipped_PostToolUse_RoslynMatchersCoverBareAndPluginPrefixes` selects on. Proposed text: "Check the server_info response for the 'update' field. If 'update.updateAvailable' is true, briefly tell the user that a newer version of the Roslyn MCP plugin is available (mention the current and latest versions) and suggest running /roslyn-mcp:update. If 'update.updateAvailable' is false, respond with 'ok' and do not mention updates unless the user explicitly asked about update status. A false value means no newer release only when 'update.checkStatus' is 'succeeded'. For any other checkStatus availability is unknown, so never claim that no update is available; if the user asked, report 'check pending' for pending or neverChecked, or the explicit failed or timedOut status."
- [ ] Red-first `HookConfigurationTests.Shipped_ServerInfoPrompt_ReadsCheckStatusForUnknownAvailability` selects the prompt containing `update.updateAvailable` and asserts that it contains `update.checkStatus`, `'succeeded'`, and each of `pending`, `neverChecked`, `failed`, `timedOut`, and does not contain `is null`. It fails against the prompt at `02db6c49`, which lacks `update.checkStatus`. That failure was observed on branch `fix/additive-4x-breaking-rework`.
- [ ] `docs/product-contract.md` § Update availability, `skills/update/SKILL.md`, and the hook agree: `checkStatus` is the authority for a `false` value.

## Evidence

- The `hooks/hooks.json` `server_info` prompt (from #1553, `77c1055d`) handles unknown status only in its "If updateAvailable is null" branch. Its false branch says "respond with 'ok' and do not mention updates" and never reads `checkStatus`.
- PR #1663 restores the v4.2.1 boolean, so a 4.x server never emits `null` and that branch cannot run. The unfinished-check case falls through to the silent false branch. This is 4.2.1 behavior, so nothing breaks, but #1553's hook improvement is lost. Source: PR #1663 cold review.

## Context

- 5.0 follow-up: `server-info-update-available-next-major-nullable` makes the prompt also treat `null` as unknown when the field becomes nullable.
