# ADR 0002 — Server-configured sanctioned-root boundary

- **Status:** Accepted (2026-08-13)
- **Deciders:** repository maintainers
- **Supersedes:** client `roots/list` as a security or discovery authority
- **Superseded by:** nothing

## Context

The host historically treated the optional MCP client Roots capability as its file-path security
boundary and as the search space for query-anchored solution discovery. That coupled server trust
to a deprecated client capability, allowed clients without Roots to bypass validation entirely,
and made a client-provided value the sole authority over server filesystem access.

Logical-path comparison also resolved only the requested leaf when it already existed. An existing
regular file below a symlink or junction ancestor therefore retained its logical path and could
appear to be inside an allowed root while its physical target was outside it.

## Decision

1. `SecurityOptions.SanctionedRoots` is the canonical server-owned boundary for path validation and
   bounded query-anchored solution discovery.
2. Empty configured roots fail closed. `PathValidationFailOpen=true` is an explicit compatibility
   escape hatch only for the empty-boundary case; it never bypasses a non-empty boundary.
3. Client- or request-provided roots are optional narrowing input. When supplied, a path must be
   inside both the configured boundary and the narrowing roots. They cannot widen the configured
   boundary or become the sole authority.
4. Paths and configured roots are canonicalized component-by-component. Every existing symlink or
   junction in the ancestor chain is resolved before comparison, including when the leaf is an
   ordinary existing file.
5. Query-anchored discovery scans only configured sanctioned roots, at the root and one directory
   level below, and never follows linked child directories. It no longer calls `roots/list`.
   File-anchored discovery retains its nearest-solution strategy but validates and canonicalizes the
   anchor before any directory enumeration and never walks above the configured boundary.
6. One-level sibling-worktree widening requires two independent opt-ins: the server operator sets
   `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true`, and the individual request sets
   `expandSanctionedRoots=true`. Request input alone cannot widen access. The effective expansion
   widens only configured roots, never request/client narrowing roots, and never widens to a whole
   filesystem root (a drive-root parent admits only `<root>-<suffix>` siblings; see the addendum).

## Compatibility and migration

This is a breaking security-default change for deployments that did not configure a boundary or
relied solely on client Roots. Before upgrading, set `ROSLYNMCP_SANCTIONED_ROOTS` to a
`Path.PathSeparator`-delimited list (`;` on Windows, `:` on macOS/Linux). A repository-scoped host
normally uses `.`. Use `ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true` only as a temporary compatibility
measure; path tools then retain the old unbounded behavior until roots are configured.

Operators that intentionally load sibling worktrees must additionally set
`ROSLYNMCP_ALLOW_ROOT_EXPANSION=true`; callers must continue to opt in on each relevant
`workspace_load` request. Leaving the server setting unset preserves the default closed boundary
even when an untrusted request supplies `expandSanctionedRoots=true`.

Clients must no longer expect `roots/list` to drive automatic solution discovery. Supply a
file-path argument, configure a search root containing exactly one solution, call `workspace_load`
explicitly, or pass `workspaceId`.

## Consequences

**Positive**

- The server operator, not an MCP client capability, owns filesystem authority.
- Legacy and request-scoped root hints can only reduce access.
- Modern clients without Roots retain deterministic, bounded discovery.
- Linked-ancestor, link-plus-parent, and discovery-child traversal no longer escape the physical
  configured boundary or disclose out-of-bound solution candidates.

**Negative**

- Existing installations must add sanctioned-root configuration or explicitly opt into fail-open.
- A configured search root containing multiple solutions produces the existing deterministic
  ambiguity response instead of guessing.

## Addendum 2026-11-03: sibling widening under a drive-root parent

Decision recorded by the operator 2026-10-15; implemented in `ConfiguredRootBoundary`.

| Case | Behavior with both opt-ins |
|---|---|
| Root's parent is an ordinary directory (`D:\Dev\repo`) | Unchanged: the whole parent is accepted. |
| Root's parent is a filesystem root (`D:\repo`, `\\srv\share\repo`, `/repo`) | Only direct siblings named `<rootName>-<non-empty suffix>` are accepted (`D:\repo-audit-x\...`, `D:\repo-x`). `D:\repo2`, `D:\repository`, `D:\repo-`, `D:\other`, nested `D:\other\repo-x` and the drive itself stay rejected. Never the whole drive. |
| Root is itself a filesystem root (`D:\`, `/`) | No parent, no widening (the prefix `-` is never synthesized). `/` on Linux behaves like `D:\`. |

- Matching uses the boundary's platform comparison (case-insensitive on Windows, ordinal elsewhere).
- Paths are canonicalized (every symlink/junction resolved) before the check, so a `<root>-x`
  link that resolves outside the boundary is rejected.
- Load, `ClientRootPathValidator`, apply-time revalidation (`ToolDispatch.RevalidateChangedPathsAsync`
  via `RootExpansionGrantRegistry`) and fork storage all call the single `ConfiguredRootBoundary`
  predicate, so load and apply cannot diverge.
- Migration: operators who previously saw sibling worktrees at `D:\<repo>-<suffix>` rejected under
  `expandSanctionedRoots` can now load them. Worktrees outside the `<repo>-` naming remain
  rejected; rename them or add their path to `ROSLYNMCP_SANCTIONED_ROOTS`. No existing accepted path
  is newly rejected.

## References

- `docs/setup.md` — operator configuration and migration
- `ai_docs/runtime.md` — environment-variable contract
- `docs/release-policy.md` — public compatibility policy
- `src/RoslynMcp.Roslyn/Services/SecurityOptions.cs` — configured options contract
- `src/RoslynMcp.Host.Stdio/Security/ConfiguredRootBoundary.cs` — canonicalization and matching
