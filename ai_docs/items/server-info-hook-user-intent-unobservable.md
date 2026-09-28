# server-info-hook-user-intent-unobservable — the server_info hook prompt branches on what the user asked, which its evaluator cannot see

**row:** `server-info-hook-user-intent-unobservable` · **pri:** `Low` · **size:** `S`

## Anchors

- `hooks/hooks.json`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs`

## Acceptance

- [ ] First verify the premise against the Claude Code prompt-hook contract: does a PostToolUse `type: "prompt"` hook's evaluator receive only the hook input (the tool call and response), not the conversation? Record the source (documentation link or a reproduction) in this row before changing anything.
- [ ] If the premise holds, the `server_info` prompt no longer conditions on "unless the user explicitly asked about update status" / "if the user asked". It states the update status whenever availability is unknown (any `checkStatus` other than `succeeded`) or `updateAvailable` is `true`, so the main agent (which does see the conversation) decides whether to mention it; it answers 'ok' only for `updateAvailable: false` with `checkStatus: 'succeeded'`.
- [ ] `HookConfigurationTests` pins the new behavior: the prompt names each `checkStatus` value and contains no user-intent condition.
- [ ] If the premise does not hold, close this row citing the evidence.

## Evidence

- Cold review of PR #1663 (commit `f3133c6b`, 2026-09-28): the prompt keeps the pre-existing clauses "do not mention updates unless the user explicitly asked about update status" and "if the user asked, report 'check pending' ...". The reviewer judged that the model evaluating a prompt hook receives only the hook-input JSON, so the "user asked" branch can never be taken.

## Context

- Pre-existing: the prompt before #1553 carried the same clause; #1663 rewrote the checkStatus logic and kept it. Not a 4.3.0 blocker: the effect is that the hook never volunteers "check pending" and the main agent must read `update.checkStatus` itself.
- `hooks/hooks.json` is release-managed: the edit needs the operator's `.release-managed-edit-allowed` sentinel (see `ai_docs/workflow.md` § Release-managed file guard).
