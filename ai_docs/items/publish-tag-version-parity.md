# publish-tag-version-parity — Match release provenance to the canonical version

**row:** `publish-tag-version-parity` · **pri:** `High` · **size:** `M`

## Anchors

- `.github/workflows/publish-nuget.yml`.
- New `eng/verify-publish-version-context.ps1`.
- New `tests/RoslynMcp.Tests/PublishVersionContextTests.cs`.

## Acceptance

- [ ] Normalize tag and release event names from `vX.Y.Z` and compare them to the canonical version already validated across all six version files.
- [ ] Fail before build, pack, or push when tag/release provenance and package version differ.
- [ ] Let manual dry-run validation use the canonical repository version without inventing a tag.
- [ ] One event-context matrix covers matching/mismatched tag, matching/mismatched release, malformed input, and manual dry-run.

## Evidence

- The publish workflow validates internal version-file parity but never compares a pushed tag or published GitHub release name with that version; a mistag can publish mismatched provenance or silently skip a duplicate package.
