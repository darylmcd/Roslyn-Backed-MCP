# restore-package-root-authoritative-read-fail-closed — Keep unreadable assets distinct from an absent recorded root

**row:** `restore-package-root-authoritative-read-fail-closed` · **pri:** `High` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/RestoreStalenessDetector.cs:409`
- `src/RoslynMcp.Roslyn/Services/RestoreInvocationPlanner.cs:154`

## Acceptance

- [ ] Reproduce an existing malformed/unreadable active assets file selecting a custom package root; distinguish it from legitimate missing/rootless assets.
- [ ] Prevent a required restore from silently dropping recorded --packages authority when its source cannot be read; preserve an explicitly separate best-effort drift detector if needed.
- [ ] Prove zero commands before the safe refusal and retain valid nullable-root/cancellation/layout controls; rederive any public refusal/migration implications before implementation.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. TryReadRestorePackagesPath catches read/JSON failures and explicitly treats them as a first restore with no --packages override. The shared planner consumes that nullable result. JSON kind guards are delivered; distinguishing unreadable authority is a different remaining mechanism.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
