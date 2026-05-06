---
name: promote-tier
description: "Flip a tool, resource, or prompt's support tier between `stable` and `experimental` atomically across both source-of-truth sites (`[McpToolMetadata]` annotation and `ServerSurfaceCatalog` partial entry). Use when: `/publish-preflight` Step 8 surfaces a `recommendation: \"promote\"` row in the promotion scorecard, a release-cut needs to flip a tier on a single tool/resource/prompt, or a tier flip needs to be reverted. Maintainer-side skill — not shipped to plugin consumers. Replaces the manual `Edit:` checklist `/publish-preflight` Step 8 surfaces today (pieces A and B shipped in PR #496; this is piece C)."
user-invocable: true
argument-hint: "<tool-or-resource-or-prompt-name> <stable|experimental>"
---

# Promote Tier

You flip the support tier of one MCP tool, resource, or prompt atomically across BOTH source-of-truth sites: the `[McpToolMetadata("category", "tier", ...)]` attribute (or its resource/prompt equivalent on the catalog entry) AND the matching catalog row. The `SurfaceCatalogTests` parity check enforces the dual-write contract — an edit that touches one site without the other fails the test, which is the safety net for this skill.

This is a **maintainer-side** skill (lives at `.claude/skills/promote-tier/`, NOT `skills/promote-tier/`). It is invoked by the maintainer at release-cut time after `/publish-preflight` Step 8 surfaces promotion candidates. It is NOT shipped to plugin consumers.

## Why this exists

Today `/publish-preflight` Step 8 emits a manual `Edit:` checklist for each promotion-recommended row:

```
- workspace_drift_check (tool, workspace) — currentTier=experimental, recommendation=promote
    Edit: src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs
    Edit: src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs
    Verify: dotnet test --filter SurfaceCatalogTests
```

The maintainer then opens both files and hand-flips two literals. Pieces A (the `/publish-preflight` Step 8 gate) and B (the promotion scorecard JSON output) shipped in PR #496. This skill is piece C — the mechanical replacement for the hand-edit step. One invocation flips both literals and runs the parity test.

## Input

`$ARGUMENTS` is two space-separated tokens:

1. **Name** — the tool, resource, or prompt name as it appears in `ServerSurfaceCatalog.*` (e.g. `workspace_drift_check`, `server_catalog_full`, `explain_error`). Kebab-snake, lowercase.
2. **Target tier** — `stable` or `experimental`. The skill looks up the current tier first; if the requested tier matches the current tier, it refuses (no-op).

Example: `/promote-tier workspace_drift_check stable`

If either argument is missing, refuse with a one-line usage message.

## Preconditions (HARD GATES — refuse if any fail)

1. **Working tree is clean** for the target source files (no unstaged edits to the tool/resource/prompt file or the catalog partial). If dirty, refuse: `"Refusing: working tree has unstaged edits to <file>. Commit or stash before flipping tier."`.
2. **Name resolves to exactly ONE entry** in `ServerSurfaceCatalog`. Zero matches → refuse with a "did you mean?" hint that lists the closest 3 names. Multiple matches → refuse with the conflicting entries (indicates a catalog-integrity bug that a human should fix).
3. **Target tier is `stable` or `experimental`**. Any other value → refuse with the two valid values.
4. **Current tier ≠ target tier** — already-at-target is a no-op refusal: `"Refusing: <name> is already at tier <target>. Nothing to do."`.

## Workflow

### Step 1 — Resolve the entry kind and category

Use `mcp__roslyn__symbol_search` (preferred over `Grep`) to locate the entry in the catalog files. The `ServerSurfaceCatalog.*.cs` partials each carry an array of `Tool(...)`, `Resource(...)`, or `Prompt(...)` factory calls — find the one whose first string argument matches the input name. Capture:

- **Kind**: `tool` | `resource` | `prompt` (which factory is used).
- **Category**: 2nd argument of the factory call (e.g. `"workspace"`, `"server"`, `"prompts"`).
- **Current tier**: 3rd argument (`"stable"` or `"experimental"`).
- **Catalog-partial path**: e.g. `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs`.

If the entry is not found, refuse per Precondition 2.

### Step 2 — Resolve the implementation site (per kind)

Three resolver paths, one per kind:

#### Tools (kind == `tool`)

The implementation site is a static method annotated with `[McpServerTool(Name = "<name>")]` AND `[McpToolMetadata("<category>", "<tier>", ...)]`. To find it:

1. Use `mcp__roslyn__symbol_search` with the search hint `<name>` to find the static method whose `[McpServerTool]` attribute carries `Name = "<name>"`. (Method names usually mirror the tool name in PascalCase, but not always — the attribute is the source of truth.)
2. Read the file. Locate the `[McpToolMetadata(...)]` attribute literal on that method. The tier literal is **parameter index 1** (0-based: `category`, `tier`, `readOnly`, `destructive`, `summary`).

#### Resources (kind == `resource`)

Resources are simpler — the tier lives ONLY in the catalog entry's 3rd argument. The `ServerResources.cs` / `WorkspaceResources.cs` files do not carry a tier marker on the `[McpServerResource]` attribute itself. Skip Step 3's "implementation-site edit" entirely; only the catalog edit applies.

(Cross-check at impl time by reading `src/RoslynMcp.Host.Stdio/Resources/ServerResources.cs` — if a future PR adds a tier marker to the resource attribute, this skill must be updated to handle it. Today: catalog-only.)

#### Prompts (kind == `prompt`)

Same as resources today — the tier lives only in the catalog. Implementation files are at `src/RoslynMcp.Host.Stdio/Prompts/RoslynPrompts.*.cs`. Cross-check by reading the file; if a tier marker is found on the `[McpServerPrompt]` attribute or a paired metadata attribute, edit it. Today: catalog-only.

### Step 3 — Edit the implementation site (tools only)

For tools, use `Edit` on the source file:

- `old_string`: the attribute literal anchor — `McpToolMetadata("<category>", "<currentTier>",`
  - Note: the literal is `McpToolMetadata(...)` without a leading `[` because the attribute often appears as the second attribute in a `[Attr1, Attr2]` pair (e.g. `[McpServerTool(...), McpToolMetadata("workspace", "experimental", ...)]`). Anchoring on `[McpToolMetadata` would miss those.
  - Include enough trailing context (typically the `<readOnly>, <destructive>,` literals plus the next-line summary) to make the match unique within the file, but stay surgical — do NOT replace the whole attribute block.
- `new_string`: identical, with `<currentTier>` flipped to `<targetTier>`.

Verify the match is unique by reading the file first (or relying on `Edit`'s uniqueness check). If the match is not unique, escalate the `old_string` with more surrounding context until it is.

For resources and prompts, skip this step.

### Step 4 — Edit the catalog entry

Use `Edit` on the catalog partial file (e.g. `ServerSurfaceCatalog.Workspace.cs`):

- `old_string`: the full factory call line, e.g.
  `Tool("workspace_drift_check", "workspace", "experimental", true, false, "Compare the in-memory workspace snapshot...`
  Include enough context to make the match unique (the name + category + tier prefix is usually sufficient since names are unique catalog-wide).
- `new_string`: identical, with the tier flipped.

### Step 5 — Run `SurfaceCatalogTests` to confirm parity

The `SurfaceCatalogTests.McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` test asserts that every `[McpToolMetadata]` attribute matches its catalog entry on every field. This is the dual-write safety net.

Run:

```bash
dotnet test --filter SurfaceCatalogTests --nologo --no-restore
```

(Or, when the workspace is loaded, prefer `mcp__roslyn__test_run --filter "SurfaceCatalogTests"` for faster feedback.)

**Pass** → both edits landed correctly; the dual-write contract holds.

**Fail** → one of the edits is inconsistent. Read the failure message: it names the offending tool and the field that drifted. Fix and re-run. **DO NOT proceed past this step on failure** — a half-flipped tier silently misadvertises the surface.

### Step 6 — Report

Emit:

```
Promoted <name> (<kind>, <category>) from <currentTier> to <targetTier>:
  Edit: <impl-site-path>           (skipped for resource/prompt)
  Edit: <catalog-partial-path>
  Verify: SurfaceCatalogTests        PASS

Next: stage the two edits, commit, and ship the tier flip in the next release. Run `/draft-changelog-entry` if a changelog fragment is desired.
```

## Non-goals

- **Do NOT edit `CHANGELOG.md`, `changelog.d/*.md`, the README.md surface counts, or any other doc.** Tier flips are tracked in `CHANGELOG.md` only when the maintainer chooses to call them out — usually as a single-line "Maintenance" or "Changed" entry — and that's `/draft-changelog-entry`'s job.
- **Do NOT commit, push, or open a PR.** The caller stages the edits and ships them via `/ship` (typically batched with other release-cut bookkeeping).
- **Do NOT flip the tier of multiple entries in one invocation.** One name per call. Bulk promotion is intentionally out of scope — each promotion is a release-quality decision that warrants individual review.
- **Do NOT touch other fields on the attribute or catalog entry.** Only the tier literal at parameter index 1. Drift in other fields is a separate concern and surfaced by a different parity test.

## Refusal cases (explicit)

- **Name not found** → refuse with the exact name and the closest 3 catalog names (Levenshtein-1 if any).
- **Name found in two catalog partials** → refuse with the two file paths (catalog-integrity bug).
- **Already at target tier** → refuse with `"<name> is already <target>. No-op."`.
- **Working tree dirty on the target files** → refuse per Precondition 1. Do not auto-stash.
- **Test failure post-edit** → STOP. Do not partially-revert. Report the test failure verbatim and let the maintainer decide whether to revert the edits or fix forward.
- **Unsupported kind** → if a future entry kind appears (e.g. a hypothetical `[McpServerSlashCommand]`), refuse with: `"Unsupported entry kind '<kind>'. The promote-tier skill currently handles tool, resource, and prompt only. Add a resolver path and round-trip test before flipping this kind."`

## Example

**Input:** `/promote-tier workspace_drift_check stable`

**Resolution:**

```
Resolved workspace_drift_check:
  kind: tool
  category: workspace
  current tier: experimental
  catalog entry: src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.Workspace.cs:20
  impl site: src/RoslynMcp.Host.Stdio/Tools/WorkspaceDriftTool.cs:24 ([McpToolMetadata("workspace", "experimental", true, false, ...)])

Editing impl site... ok
Editing catalog entry... ok
Running SurfaceCatalogTests... PASS

Promoted workspace_drift_check (tool, workspace) from experimental to stable.
```

**Reverting the same flip (round-trip):**

**Input:** `/promote-tier workspace_drift_check experimental`

```
Resolved workspace_drift_check:
  kind: tool
  ... current tier: stable

Editing impl site... ok
Editing catalog entry... ok
Running SurfaceCatalogTests... PASS

Reverted workspace_drift_check (tool, workspace) from stable to experimental.
```

The round-trip is bit-identical — the test that pairs with this skill (`tests/RoslynMcp.Tests/Skills/PromoteTierRoundTripTests.cs`) asserts this contract: a forward + reverse promotion leaves the source tree exactly as it found it.

## Distinct from related skills

- **`/publish-preflight`**: surfaces `recommendation: "promote"` rows in Step 8 and emits the `Edit:` checklist. This skill replaces the manual edits it suggests.
- **`/draft-changelog-entry`**: writes ONE `changelog.d/<row-id>.md` fragment. After a tier flip ships in a release, the maintainer may invoke this to record the promotion in the changelog (optional — minor tier flips often ride in a `## [X.Y.Z]` section's "Maintenance" bullet without a dedicated fragment).
- **`/bump`**: rolls accumulated `changelog.d/*.md` fragments into a tagged release. Tier flips are visible to consumers from this point.
- **`/release-cut`**: the atomic release pipeline (`/bump` → `/ship` → tag → reinstall). A typical release-cut sequence is: `/publish-preflight` → `/promote-tier` (per recommended row) → `/release-cut`.

## Implementation notes (for future maintainers of this skill)

- The `[McpToolMetadata]` attribute's parameter shape is defined at `src/RoslynMcp.Host.Stdio/Catalog/McpToolMetadataAttribute.cs`. Today the parameter order is `(category, supportTier, readOnly, destructive, summary)` — `supportTier` is parameter index 1 (0-based). If the attribute shape ever changes, this skill's "Step 3 — Edit the implementation site" must be updated to match.
- The catalog factory functions (`Tool`, `Resource`, `Prompt`) live in `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`. Same parameter-shape caveat — `tier` is parameter index 2 (0-based: `name`, `category`, `tier`, ...).
- The `SurfaceCatalogTests.McpToolMetadata_RequiredOnEveryTool_MatchesCatalogEntry` test is the dual-write safety net. If a future refactor splits the catalog or reshapes the attribute, that test must continue to assert dual-site parity OR this skill needs a different verification strategy.
