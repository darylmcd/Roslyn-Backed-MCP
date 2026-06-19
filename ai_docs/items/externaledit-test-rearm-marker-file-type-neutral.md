# externaledit-test-rearm-marker-file-type-neutral — Watcher staleness test re-arm marker assumes C# comment syntax

**row:** `externaledit-test-rearm-marker-file-type-neutral` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:322` (dropped-event re-touch marker)

## Acceptance

- [ ] The dropped-event re-touch appends a file-type-neutral marker (e.g. a trailing newline/whitespace, or a marker chosen by file extension) so the helper is safe for the `.csproj`/`.props`/`.targets`/`.sln` files the watcher also tracks.

## Evidence

- Code-quality review of `ci-flaky-fswatcher-staleness-test` (2026-06-19 top-n-remediation): the re-arm writes `// watcher re-arm {guid}` — invalid markup if the helper is ever invoked with a project/solution file. Bounded today (the `finally` restores the original content and the only caller passes a `.cs` file), so low severity.

## Context

The re-touch loop is the dropped-OS-event guard added by the flaky-test fix. It is correct for the current `.cs` tracked file; the smell is the hardcoded `//` comment syntax in a helper that accepts an arbitrary `trackedFile` path.
