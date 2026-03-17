# Strategic Roadmap Decisions

This roadmap complements the operating contract in `docs/product-contract.md` and the day-to-day agent playbook in `AGENTS.md`.

## Release 1 Decision

Optimize the product for local stdio deployment on developer workstations.

That means:

- the stable contract is defined around the current stdio host
- destructive operations stay bounded and local
- remote hosting requirements do not block the first production release

## Unsaved Buffer And Live Workspace Parity

Decision: do not claim parity with Visual Studio-backed servers in the current host.

Reason:

- `MSBuildWorkspace` is sourced from on-disk state
- unsaved editor buffers and live IDE diagnostics require a different integration path

Future direction:

- add a separate editor-backed or Visual Studio-backed host if live parity becomes a product requirement

## HTTP/SSE Hosting

Decision: defer to a second host project.

Reason:

- remote deployment needs auth, authorization, observability, policy enforcement, and tenancy boundaries
- mixing those concerns into the current local-first host would slow the release and blur the support story

Future direction:

- keep `Core` and `Roslyn` transport-agnostic
- build an HTTP/SSE host only when the operational requirements are approved and staffed

## Large-Solution Performance Strategy

Decision: ship bounded local behavior now, then add indexing/caching later if real users need it.

Release-1 strategy:

- per-workspace execution gating
- bounded command output
- bounded related-test scans
- bounded generated-document discovery
- workspace-count cap

Post-release candidates:

- persistent symbol/index cache
- incremental background indexing
- opt-in warmup for enterprise solutions
- separate performance profile for remote hosting

## Execution Tracks And Primary Touchpoints

Use these tracks to quickly identify where to implement follow-up work:

- transport/hosting track:
	- primary: `src/Company.RoslynMcp.Host.Stdio/`
	- future: additional host project for HTTP/SSE
- semantic/refactoring engine track:
	- primary: `src/Company.RoslynMcp.Roslyn/`
- contract/tiering track:
	- primary: `src/Company.RoslynMcp.Core/`, `docs/product-contract.md`, `README.md`
- hardening/release track:
	- primary: `tests/Company.RoslynMcp.Tests/`, `eng/verify-release.ps1`, `docs/release-policy.md`
