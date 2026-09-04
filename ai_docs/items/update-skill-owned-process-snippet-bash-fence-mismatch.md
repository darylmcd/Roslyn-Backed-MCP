# update-skill-owned-process-snippet-bash-fence-mismatch — update-skill's owned-process-identity snippet is fenced as bash but is raw PowerShell

**row:** `update-skill-owned-process-snippet-bash-fence-mismatch` · **pri:** `Low` · **size:** `S`

## Anchors

- `.claude/skills/update/SKILL.md:74-80`

## Acceptance

- [ ] The `$ownedPid = ...; $env:ROSLYNMCP_REINSTALL_PROCESS_ID = ...` snippet either runs under a `pwsh -NoProfile -Command "..."` wrapper (matching the convention at `SKILL.md:68-70`) or the fence is relabeled ```powershell so it isn't presented as bash-runnable.
- [ ] Copy-pasting the block verbatim into a bash/cmd shell no longer produces `$env:` / `Get-Process` syntax errors.

## Evidence

Cold `implementation-reviewer` on `tool-update-owned-process-shutdown` (PR #1439, merged): the new ```bash fenced block at `SKILL.md:74-80` contains raw PowerShell (`$env:ROSLYNMCP_REINSTALL_PROCESS_ID = $ownedPid`, `Get-Process -Id $ownedPid`) with no `pwsh -NoProfile -Command` wrapper, unlike the established pattern in the same file's sibling block (`:68-70`).
