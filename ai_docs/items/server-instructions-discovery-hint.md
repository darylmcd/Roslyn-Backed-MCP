# server-instructions-discovery-hint — Ship ServerInstructions discovery hint

**row:** `server-instructions-discovery-hint` · **pri:** `High` · **size:** `S`

## Anchors

- `src/RoslynMcp.Host.Stdio/Program.cs:45-53` (McpServerOptions — sets only ServerInfo Name/Title/Version today)
- `tests/RoslynMcp.Tests/` (new host-level test: instructions non-empty, ≤2,048 chars)

## Acceptance

- [ ] initialize result carries a non-empty `instructions` string ≤2,048 chars (Claude Code truncates at 2KB)
- [ ] Content is discovery-hint shaped: tool-category map (analysis / refactoring preview→apply / workspace lifecycle / validation), workspace_load-first bootstrap, when-to-search guidance
- [ ] Test guards non-empty + the 2,048-char cap so future edits cannot silently exceed the client truncation

## Evidence

- Wire probe: `instructionsChars: 0`; Claude Code tool search loads only tool names + server instructions at session start, so 173 deferred tools have zero discovery text in the dominant client — see `ai_docs/reports/20260813T025903Z_roslyn-backed-mcp_mcp-token-overhead-and-conformance-audit.md` §1, §5

## Context

Highest practical impact per unit effort of the whole audit. API exists in SDK 1.4.x — no upgrade needed. This row is also the relocation target for `method-description-diet` (guidance moved out of per-tool descriptions needs this to exist first).
