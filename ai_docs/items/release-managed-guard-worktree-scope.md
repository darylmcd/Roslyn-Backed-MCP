# release-managed-guard-worktree-scope — Resolve release-managed guard paths and sentinel per worktree

**row:** `release-managed-guard-worktree-scope` · **pri:** `High` · **size:** `S`

## Anchors

- `eng/guard-release-managed-files.ps1:1-114`
- `ai_docs/prompts/backlog-sweep-addenda.md:215-261`
- `ai_docs/workflow.md:57-86`
- `tests/RoslynMcp.Tests/HookConfigurationTests.cs:156-300`
- `tests/RoslynMcp.Tests/ReleaseManagedFileGuardDocumentationTests.cs`

## Acceptance

- [ ] Scope: the guard covers only checkouts of this repository, meaning the primary and its linked worktrees. A checkout belongs to this repository when `git rev-parse --path-format=absolute --git-common-dir` returns the same directory for it and for the project root (`CLAUDE_PROJECT_DIR`, else the script's own repo root). A path outside this repository's checkouts, whether in another repository or in none, is allowed without a sentinel. That covers a `$managedExact` path in another repository (its changelog, version props or root manifest, or a plugin checkout's hook config), which is allowed today because the path stays absolute. It also covers a `BannedSymbols.txt` outside this repository, which the basename rule blocks today with an instruction to create a sentinel in this repository. Tests with a separate temp git repository: its CHANGELOG.md stays allowed (passes today and must keep passing, since a nearest-checkout rule without the common-dir check would block it); its `BannedSymbols.txt` is allowed (red today).
- [ ] When the project root is not a git checkout, paths are compared against `CLAUDE_PROJECT_DIR` exactly as today. A relative `file_path` resolves against `CLAUDE_PROJECT_DIR` (else the script's repo root), never the process working directory. The relative cases at `HookConfigurationTests.cs:162-163` (the managed version-props path exits 2 and an unmanaged `src/` source path exits 0, with `CLAUDE_PROJECT_DIR` set to a non-git temp dir) keep their exits no matter where the test process runs and whether the real checkout has a live sentinel.
- [ ] For a target inside a checkout of this repository, the root is that checkout's top level (`git -C <nearest existing ancestor> rev-parse --show-toplevel`), and the relative path is computed against it. An edit to a version source or the shipped hook config inside `<primary>/.worktrees/<id>/` is therefore release-managed. Red-first test in a temp repo with a linked worktree: today that edit is allowed (fail-open); after the fix it is blocked without a sentinel.
- [ ] The sentinel is read from that same root, and the denial message prints that root's sentinel path. Tests: a worktree edit with the sentinel only in the primary is blocked (red today: the edit is allowed); a worktree `BannedSymbols.txt` edit is not unlocked by a primary sentinel (red today: it is); with the sentinel in the worktree the edit is allowed; a worktree sentinel never unlocks the primary.
- [ ] The managed set itself is unchanged. The `BannedSymbols.txt` basename match at `:91` still guards every copy inside this repository's checkouts, and a worktree executor unlocks it with its own worktree sentinel.
- [ ] The hook stays well inside its 10 s cap despite the added `git` calls. Re-measure it and update the AGENTS.md Validation runtime row if the figure moves.
- [ ] The script header comment (`:7`, which still names `$env:CLAUDE_PROJECT_DIR` as the sentinel location), the addenda Hooks section and `ai_docs/workflow.md` § Release-managed file guard (including its bypass command) describe per-checkout resolution and the repository scope. They state that the sentinel goes in the root of the checkout being edited: addenda `:259-261` gives no location today, and the addenda's "any BannedSymbols.txt" (`:228`) becomes "any BannedSymbols.txt in this repository's checkouts". The guard documentation tests stay green.

## Evidence

- `eng/guard-release-managed-files.ps1:57` sets `$repoRoot = $env:CLAUDE_PROJECT_DIR`, which is the primary checkout for every subagent launched from it. `:60-66` strips that root to form `$relative` and leaves any other path (relative or absolute) as given. `:88-94` matches exact paths or the `bannedsymbols.txt` basename, `:98` builds the sentinel path from the same root, and `:114` tells the caller to create it there.
- The hook's matcher is `Edit|Write|MultiEdit` (`.claude/settings.json:50-58`), so it fires for every path a Roslyn-rooted session edits, other repositories included. Today a path outside `CLAUDE_PROJECT_DIR` stays absolute, so only the `BannedSymbols.txt` basename can match it. That match is a false positive:
  - The guarded set is this repository's release inputs. `ai_docs/workflow.md` § Release-managed file guard calls the hook repo-local and lists `BannedSymbols.txt` as the "Repo-wide banned-API list (release-critical analyzer input)".
  - `Directory.Build.props:32` wires only this repository's root copy into its analyzers (`AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt"`), and at `02db6c49` that root copy is the only tracked `BannedSymbols.txt`.
  - Another repository's ban list feeds that repository's own build. This repository's release and version-drift checks never read it.
  - The denial tells the agent to create the sentinel in this repository's root, which unlocks this repository's release-managed files for 30 minutes and protects nothing in the other repository.
- Consequence 1: a worktree executor has to create `.release-managed-edit-allowed` in the shared primary root. That unlocks release-managed edits for every session and worktree for 30 minutes (retro 2026-09-27: 4 occurrences; the row was promised during a run and never filed).
- Consequence 2 (found while re-verifying at `19ccd61b`, and separately by plan `20260926T234932Z`'s follow-up): an exact-path file in a worktree yields `relative = .worktrees/<id>/directory.build.props`, which never matches `$managedExact`. Version sources and `hooks/hooks.json` are therefore unguarded in worktrees, while every `BannedSymbols.txt` (basename match) is blocked.
- Addenda `:259-261` ("an initiative that intentionally edits it must first create the fresh `.release-managed-edit-allowed` sentinel") gives no location.

## Context

- High: the only workaround widens a write-unlock to every concurrent session, and the fail-open half lets a worktree edit version sources silently.
- `argument-exception-public-message-guard` creates `src/BannedSymbols.txt`, which the basename rule guards. It depends on this row, and its Acceptance uses the checkout-local sentinel this row introduces instead of the primary-root workaround.
- 2026-09-28: merged with the duplicate `release-guard-worktree-root` (retro 2026-09-27, PR #1655), which carried the repository-scope rule, the relative-path rule and the red-first test list. This id is kept because plan `20260926T234932Z_backlog-remediate` records it as a spin-off row.
