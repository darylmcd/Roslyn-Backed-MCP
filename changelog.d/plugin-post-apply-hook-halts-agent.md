---
category: Fixed
---

- **Fixed:** The shipped post-apply verification reminder is now a non-blocking `type: command` hook that prints `hookSpecificOutput.additionalContext` and exits 0, replacing the PostToolUse prompt hook whose non-`ok` reply ended the agent turn after `*_apply` calls, and the `server_info` update-notice prompt hook now sets `continueOnBlock` so its reply is fed back instead of ending the turn; structural and executed-command tests cover both, while live verification of a three-apply chain is pending until the refreshed plugin is installed (`plugin-post-apply-hook-halts-agent` stays open for that check).
