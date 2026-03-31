# Copilot Instructions

Implementation quality and safety rules for this repository.
For session bootstrap and workflow, follow `AGENTS.md` first.

## Code Style (C#)

- **Namespaces:** file-scoped (`namespace Foo;`), not block-scoped — enforced as warning in `.editorconfig`
- **var:** use `var` only when type is apparent; use explicit types elsewhere
- **Braces:** omit only for single-line; use braces when multiline
- **Expression bodies:** prefer for single-line properties and accessors
- **Usings:** System namespaces first; no blank line between using groups; remove unused usings before merge
- **Nullability:** enable nullable reference types; use `??` and null-conditional operators; no unchecked `!` except in tests
- **Async:** all async methods suffixed `Async`; no `async void` except event handlers

## Layering Rules

- `RoslynMcp.Host.Stdio` may depend on `RoslynMcp.Core` and `RoslynMcp.Roslyn`
- `RoslynMcp.Roslyn` may depend on `RoslynMcp.Core`
- `RoslynMcp.Core` has no intra-project dependencies — DTOs and contracts only
- Do NOT let raw Roslyn types (`SyntaxNode`, `ISymbol`, etc.) cross the `RoslynMcp.Core` boundary
- Do NOT put transport-specific logic in `RoslynMcp.Roslyn`
- `stdout` is reserved for MCP protocol traffic — use `stderr` for all operational logging

## Mutation Safety

- All mutations must go through preview → apply flow
- Pass and propagate `workspaceId` on all workspace-scoped operations
- Reject or regenerate a preview if the workspace version has changed between preview and apply

## Testing Requirements

- New tools and analysis methods need integration test coverage in `tests/RoslynMcp.Tests/`
- Prefer integration tests over unit tests — mock-only tests have previously masked real failures
- Use sample solutions in `samples/` for workspace-level tests
- Run `dotnet test RoslynMcp.slnx --nologo` before declaring work merge-ready
- Current coverage baseline: ~49.8% line / ~37.6% branch — do not regress it

## Safety Guardrails

- Never write to `stdout` from service or domain code — MCP protocol owns that stream
- Never skip `verify-release.ps1` validation for code changes
- Never merge with failing CI
- Do not add new public stable-surface tools without updating `server_info` surface counts and `roslyn://server/catalog`
- Do not evolve stable-surface DTOs in a breaking way (additive changes only)
- Do not hard-code solution paths or machine-specific paths in source or tests

## Naming Conventions

- MCP tool names: `snake_case` (e.g., `find_references`, `apply_code_action`)
- MCP resource URIs: `roslyn://<scope>/<name>` pattern
- DTO classes: PascalCase, suffix with `Request` / `Result` / `Response` as appropriate
- Service interfaces: `I<Domain>Service` pattern
- Test classes: `<Subject>Tests` suffix

## Security

- Do not log user code or file contents to stderr at Info level — Debug only
- Do not accept arbitrary shell command execution from MCP tool parameters
- Validate all file paths are within the loaded workspace root before file I/O
