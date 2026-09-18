# sanctioned-roots-cwd-default-undocumented — sanctioned-roots-cwd-default-undocumented

**row:** `sanctioned-roots-cwd-default-undocumented` · **pri:** `Medium` · **size:** `S` · **deps:** `boundary-rejection-redacted-to-generic-schema-error`

## Anchors

- `.claude-plugin/mcp.json` — the shipped `ROSLYNMCP_SANCTIONED_ROOTS` default.
- `README.md` — document the boundary and the supported override.

## Acceptance

- [ ] The published plugin documents that `ROSLYNMCP_SANCTIONED_ROOTS: "."` resolves to the session's working directory, so a session started outside the target repo can load no workspace in it.
- [ ] A supported way to analyze a repo other than the session cwd is documented — a multi-root value, a variable the host expands to the project directory, or an explicit operator override — with its security trade-off stated.
- [ ] The default stays fail-closed; any change to the shipped default is recorded as an ADR with a migration note (published artifact, Directive #4 contract-care).

## Evidence

- `.claude-plugin/mcp.json` ships `"env": {"ROSLYNMCP_SANCTIONED_ROOTS": "."}`.
- Confirmed 2026-09-18 in a session with cwd `C:/Users/daryl/.claude`: `server_info` reported `pathBoundary: {configuredRootCount: 1, enforcing: true, failOpen: false}`; a `workspace_load` of a solution under `C:/Code-Repo/` was refused while a solution under `C:/Users/daryl/.claude/` loaded — the boundary had resolved to the session cwd.
- `workspace_load`'s `expandSanctionedRoots` parameter does not cover this: it widens one level for sibling worktrees only, and only when the operator also sets `ROSLYNMCP_ALLOW_ROOT_EXPANSION=true`.

## Context

Not a defect in the default itself. For the common case — a session started in the repo being analyzed — `"."` resolves to that repo and is exactly the right fail-closed scope. The gap is that the behavior is undocumented and has no documented escape hatch, so a multi-repo or tooling-rooted session sees a server that can load nothing and no way to find out why. Pairs with `boundary-rejection-redacted-to-generic-schema-error`, which is why the cause is currently invisible at the call site; fixing that one first may reduce this to a documentation change.

Scope guard: this row is documentation plus a supported override. Do not redesign the boundary model under it.
