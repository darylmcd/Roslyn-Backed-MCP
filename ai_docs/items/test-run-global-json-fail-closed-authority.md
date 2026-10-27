# test-run-global-json-fail-closed-authority — Distinguish invalid runner configuration from no configuration

**row:** `test-run-global-json-fail-closed-authority` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetTestModeResolver.cs:20`
- `src/RoslynMcp.Roslyn/Helpers/DotnetTestModeResolver.cs:47`

## Acceptance

- [ ] Select the nearest configuration through a complete owned search; malformed/unreadable configuration is a safe typed refusal, not an inferred VSTest default.
- [ ] Preserve comments/trailing commas, valid native/VSTest selection, actual missing configuration and caller cancellation.
- [ ] Re-derive current authorization integration separately from the unapproved path-policy architecture; do not make that architecture a prerequisite.

## Evidence

- Source audit at main `fc5bb88676214a942daa727c7ca88ac8dd34b61c`. ReadsNativeMtpRunner explicitly logs and returns false for IOException, UnauthorizedAccessException and JsonException. The branch replaces this with required prepared configuration state; that interface redesign is not approved merely by this defect.
- Source: closed PR #1806 head `d333c2e9`, kept by GitHub as `refs/pull/1806/head` (archive branch and tag deleted 2026-10-27). No build/test ran in this content audit; historical receipts are not current runtime qualification.
