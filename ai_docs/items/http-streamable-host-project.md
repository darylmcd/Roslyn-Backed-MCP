# http-streamable-host-project — sibling MCP Streamable HTTP host (parked)

**row:** `http-streamable-host-project` · **pri:** `Defer` · **size:** `—` <!-- cache — the backlog row is canonical for pri/size; refresh on open if they disagree -->

## Anchors

- new `src/RoslynMcp.Host.Http/` project; `Core` + `Roslyn` boundaries are already transport-agnostic per `ai_docs/architecture.md`

## Acceptance

- [ ] (on unblock) `src/RoslynMcp.Host.Http/` sibling project reusing `Core` + `Roslyn`, exposing the same surface over MCP Streamable HTTP transport

## Evidence

- `docs/roadmap.md` § HTTP/SSE Hosting (deferred to a second host project); MCP spec § Streamable HTTP transport (2025-03 stable). Source: 2026-05-05 MCP-best-practices comparison §3 rec H.

## Context

Unblock trigger: concrete remote-deployment driver (named users, auth/observability/tenancy plan approved and staffed). Roadmap-aligned but requires multi-week design (auth flows, per-tenant rate limiting, TLS, observability, deployment story). The current local-first product is healthy and the roadmap explicitly punts on these concerns. Re-evaluate when there is a concrete remote-deployment ask with named users / SLA expectations.
