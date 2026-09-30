# workspace-project-alias-lookup — Match project paths across filesystem aliases

**row:** `workspace-project-alias-lookup` · **pri:** `Medium` · **size:** `S`

## Anchors

- `src/RoslynMcp.Roslyn/Services/WorkspaceManager.cs:982-1010`

## Acceptance

- [ ] A project selected by path matches the loaded project when its argument uses an equivalent junction, drive alias, or canonical drive spelling.
- [ ] Name lookup preserves its current behavior; path lookup uses physical filesystem identity with platform comparison.
- [ ] A regression loads a project through one path spelling and selects it through another without selecting a different project.

## Evidence

- Code inspection 2026-09-29: the project lookup indexes lexical project paths and compares a caller path verbatim. A caller alias for the same loaded project is missed.

## Context

- Separate from workspace-load boundary canonicalization and document file filters; this is project lookup by caller-supplied path.
