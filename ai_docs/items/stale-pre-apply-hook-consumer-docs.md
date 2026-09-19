# stale-pre-apply-hook-consumer-docs — consumer docs promise a removed blocking pre-apply hook

**row:** `stale-pre-apply-hook-consumer-docs` · **pri:** `Low` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/README.md:70`
- `docs/reinstall.md:185`
- `hooks/hooks.json`

## Acceptance

- [ ] Both consumer documents describe the current post-apply advisory hooks and server-enforced preview-token validation; neither claims a pre-apply hook blocks `*_apply` calls.
- [ ] `docs/reinstall.md` points to an existing authoritative hook/configuration section or directly to `hooks/hooks.json`; it does not cite the nonexistent README `Plugin Hooks` section.
- [ ] A documentation regression check fails if either removed blocking-hook claim returns.

## Evidence

`src/RoslynMcp.Host.Stdio/README.md:70` promises pre-apply safety hooks that block apply tools without matching previews, while `docs/reinstall.md:185-187` calls the same behavior expected and points to a nonexistent README `Plugin Hooks` section. The shipped `hooks/hooks.json` contains PostToolUse advisory prompts only; preview-token enforcement is server-side.
