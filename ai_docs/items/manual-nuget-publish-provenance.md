# manual-nuget-publish-provenance — Build and verify the package pushed manually

**row:** `manual-nuget-publish-provenance` · **pri:** `High` · **size:** `S`

## Anchors

- `eng/publish-nuget.ps1`.
- `.claude/skills/publish-preflight/SKILL.md`.
- `.claude/skills/release-cut/SKILL.md`.
- New `tests/RoslynMcp.Tests/ManualNuGetPublishContractTests.cs`.

## Acceptance

- [ ] Derive the package version only from the six-file canonical release state; reject an arbitrary or mismatched caller-supplied version.
- [ ] Run the publish-mode release gate against the current checkout and create a fresh package in an owned staging directory before any push.
- [ ] Validate the staged package identity/version and never select a pre-existing `nupkg/` artifact by filename alone.
- [ ] Keep API-key handling secret-safe and preserve an explicit no-push validation mode.
- [ ] Propagate a nonzero `dotnet nuget push` exit and never print or return success after a failed native command.
- [ ] One process/source matrix proves stale-package refusal, canonical fresh-pack selection, gate failure, dry run, push argument construction, and nonzero push propagation.

## Evidence

- The documented manual publisher accepts a caller-selected `-Version`, locates any matching pre-existing package under `nupkg/`, and pushes it without build/test, version-drift, package-content, or checkout-provenance validation. It also ignores the native push exit code and prints `Done.` after a failed command because native failures are not governed by `$ErrorActionPreference` in the current PowerShell configuration.
