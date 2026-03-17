# Parity And Gap Matrix

This matrix defines what agents should treat as hard boundaries vs roadmap opportunities.

Use with:

- `docs/product-contract.md` for compatibility expectations
- `docs/roadmap.md` for deferred implementation direction
- `AGENTS.md` for operational workflow in live sessions

## Comparison Summary

| Comparison area | Current position | Release decision |
|---|---|---|
| Standalone Roslyn MCP servers | Competitive or ahead on semantic navigation, preview/apply refactoring, and build/test workflows | Ship now after hardening and contract cleanup |
| Visual Studio-backed Roslyn servers | Behind on unsaved buffers, live diagnostics, and IDE fidelity | Document as an explicit product boundary, not a hidden gap |
| MCP production best practices | Stronger now on contract discoverability, support tiers, and local operational limits; still local-first only | Ship as local stdio with remote hosting deferred to a second host |
| AI coding agent needs | Strong stable path for workspace/session management, semantic navigation, diagnostics, validation, and bounded mutation | Keep stable contract narrow and machine-readable; leave opportunistic features experimental |

## Must-Have Before Release

- explicit stable vs experimental surface
- machine-readable contract via `server_catalog`
- wrapper/integration tests for previously under-tested tool families
- workspace/session limits and failed-load cleanup
- bounded related-test scans and command timeouts
- canonical CI and publish verification path
- documented compatibility, deprecation, and release policy

## Post-Release Roadmap

- second host for HTTP/SSE with auth, policy, and observability
- editor-backed or Visual Studio-backed live workspace integration for unsaved-buffer parity
- large-solution indexing and cache strategy
- selective promotion of experimental surfaces to stable once they have stronger operational evidence

## Out Of Scope For This Release

- claiming parity with live IDE state while still using `MSBuildWorkspace`
- treating prompts as part of the compatibility-guaranteed API surface
- exposing remote deployment guidance before a dedicated remote host exists

## Agent Implications

- do not plan workflows that rely on unsaved editor state
- do not assume experimental prompt/tool shapes are version-stable
- keep mutation flows bounded and preview-first
- keep release-facing statements aligned with local stdio production scope
