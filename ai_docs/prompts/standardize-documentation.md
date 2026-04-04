# Prompt: Standardize human + AI documentation (cross-repo)

<!-- purpose: Cross-repo doc standardization prompt; defers to doc-audit skill Phase 0. -->

Portable prompt for Cursor, Claude, or other agents. **Do not duplicate the `/doc-audit` skill:** treat the skill as the canonical documentation standard and discovery engine. Run it first (see Phase 0), then extend with packaging/distribution inventory and human-docs coverage.

**Canonical reference:** Claude Code skill `doc-audit` (`~/.claude/skills/doc-audit/SKILL.md`) — Section 3 *Documentation Standard*.

---

## Full prompt (copy from the block below)

```
You are standardizing all human-facing and AI-facing documentation in this repository so it matches the current project state, removes stale references, and follows a consistent layout across repos.

Use the `/doc-audit` skill (or equivalent) as the foundation: its **Step 0** discovery, **Step 0b** context, **Section 3** target standard, and **Step 4** `.ai-doc-audit.md` format are authoritative. This prompt extends that skill with deeper packaging/distribution discovery and explicit human-docs completeness checks.

---

## Phase 0 — Discovery (invoke doc-audit skill)

1. Run the **doc-audit** skill in **`initial`** mode (or pass `initial` argument) so the repo gets full gap analysis, structural compliance vs Section 3, and an updated `.ai-doc-audit.md`.
2. **Wait for approval** on the gap analysis before making edits (per doc-audit Step 2).
3. **Optimization:** If `.ai-doc-audit.md` already exists and **Last audit** is within the **last 3 calendar days**, you may read it and skip re-running the skill **only** if the repo has had no meaningful code or config changes since that audit; otherwise run `initial` again.

---

## Phase 1 — Project mechanics auto-discovery (packaging / distribution inventory)

Go beyond doc-audit Step 0b item 5 by building a **packaging/distribution inventory**: every way this repo can be built, tested, run, packaged, installed, or shipped.

**Scan** (as applicable — skip what does not exist):

- **.NET:** `.csproj` (PackAsTool, OutputType, IsPackable), `.sln` / `.slnx`, `global.json`, `Directory.Build.props`, `.nuspec`, `eng/*.ps1` or other scripts
- **Python:** `pyproject.toml` (build-system, scripts, entry-points), `setup.py`, `requirements.txt`, `Makefile`, `tox.ini`, `noxfile.py`
- **Node / TypeScript:** `package.json` (scripts, bin, main/module), `tsconfig.json`, bundler configs
- **Rust:** `Cargo.toml` (bin/lib, workspace, features), `build.rs`
- **Go:** `go.mod`
- **Cross-stack:** `Dockerfile`, `docker-compose.yml`, `Makefile`, `.github/workflows/*` (artifacts, publish steps)

**Output:** a **markdown table** (or bullet list) with columns: `| Form | Config or source | Command(s) | Notes |` — one row per discovered form (e.g. local dev run, `dotnet publish`, global tool install, pip wheel, Docker build, CI artifact download).

---

## Phase 2 — AI-facing documentation (`ai_docs/`)

Enforce **doc-audit Section 3** quality rules for `ai_docs/`:

- No prose paragraphs over 3 sentences; prefer tables, lists, code blocks, diagrams
- No duplicate information; point to canonical files
- No stale content; remove references to deleted code paths or files
- Misplaced content → move to `docs/` or `ai_docs/archive/`
- **`ai_docs/README.md`:** index every file under `ai_docs/` with one-line descriptions; task-scoped reading guide; **stay under 150 lines**; pointers only

**Additional formatting:**

- After the **first heading** in each `ai_docs/*.md` file, add **one HTML comment** line: `<!-- purpose: one line -->` describing the file’s role (agents use this for quick scanning).
- **Cross-references:** use **relative** paths only (no `C:\` or absolute repo paths).
- **Domain references:** every `domains/*` reference must map to an existing source directory; verify on disk.
- **Stale removal:** grep `ai_docs/` for paths; verify each resolves; remove index entries for deleted prompts/procedures; align `ai_docs/runtime.md` with commands that actually work.

---

## Phase 3 — Human-facing documentation (`docs/` + root README)

1. **Ensure `docs/`** exists with **`docs/README.md`** as the human index (mandatory per doc-audit standard when `docs/` exists).
2. **For each row** in the Phase 1 packaging/distribution inventory, ensure `docs/` (or clearly linked sections from `docs/README.md`) documents:
   - **Prerequisites** (SDK/runtime, tools)
   - **Build** (every build command)
   - **Test** (every test command)
   - **Run** (every run path: from source, published binary, container, global tool, etc.)
   - **Package** (NuGet pack, wheel/sdist, npm pack, `cargo` artifacts, image build, etc.)
   - **Install** (global tool, pip install, npm -g, cargo install, pulling/using images)
   - **CI artifacts** (if CI uploads artifacts, document how to obtain them)

**Placement:** setup/install/usage narrative belongs in **`docs/`**, not `ai_docs/`. If root **`README.md`** duplicates long setup, consolidate into `docs/` and keep README as a short pointer.

**Also audit** (if present): `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md` for broken links, wrong versions, or obsolete commands.

---

## Phase 4 — Validation and finalization

1. Run **doc-audit Cross-Repository Consistency Rules** (Section 3): `docs/` not `data/docs/`, allowed root `.md` only, integration docs under `ai_docs/ecosystem/`, etc.
2. **Verify** every markdown link and path reference in edited docs resolves.
3. **If the repo has** `eng/verify-ai-docs.ps1` (or equivalent doc validation script), run it and fix failures.
4. **Update** `.ai-doc-audit.md` with final state (per doc-audit Step 4).
5. **Update** `ai_docs/README.md` index for every add/remove/rename under `ai_docs/`.
6. **Deliver** a single list: file → created | modified | moved | deleted.

---

## Constraints

- **Do not** change application code unless a doc change strictly requires it (rare).
- **Do not** create placeholder files with TODO-only content — write real content or omit.
- **Do** read code before documenting behavior.
- **Do** present **gap analysis for approval** before bulk edits (doc-audit Step 2).
- **Documentation-only repos** (no `src/` or build): still run Phase 0; Phase 1 inventory may be empty — document policy/process and repo layout instead.

---

## Deliverables

1. **Gap analysis** (before changes): structural gaps, stale refs, missing human-docs coverage vs inventory.
2. **After changes:** list of files touched with one-line summary each.
3. **Updated** `.ai-doc-audit.md` and **`ai_docs/README.md`** index.
4. **Packaging/distribution inventory** (Phase 1 output) included in the report or committed under `docs/` if the repo keeps long-form reference there.
```

---

## Optional context line (prepend when needed)

```
Repo root: <path>. Default branch: <main|master>. Doc style: Markdown tables OK. If doc-audit skill path differs, use the repo’s installed copy of the skill.
```

---

## Maintenance

When this repo’s doc layout, CI scripts, or doc-audit skill standard changes, update this prompt so Phase 1 discovery rules and Phase 4 validation stay aligned with reality.
