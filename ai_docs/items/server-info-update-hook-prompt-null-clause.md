# server-info-update-hook-prompt-null-clause — add an explicit null-as-unknown clause to the server_info hook prompt

**row:** `server-info-update-hook-prompt-null-clause` · **pri:** `Low` · **size:** `S`

## Anchors

- `hooks/hooks.json`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs`

## Acceptance

- [ ] The `server_info` PostToolUse prompt states that a `null` `update.updateAvailable` means availability is unknown (report the check status, never claim "no update"), alongside the existing `checkStatus` guidance.
- [ ] `HookConfigurationTests.Shipped_ServerInfoPrompt_ReadsCheckStatusForUnknownAvailability` also requires the `null` clause.
- [ ] The edit uses the `.release-managed-edit-allowed` sentinel in the executor's own checkout and removes it before commit.

## Evidence

- Split from `server-info-update-available-next-major-nullable` (shipped): that PR made `updateAvailable` `bool?` but could not edit the release-managed `hooks/hooks.json` (the auto-mode classifier denied creating the override sentinel). The current prompt already treats every non-`succeeded` status as unknown, so behavior is correct for `null`; only the explicit wording is missing.
