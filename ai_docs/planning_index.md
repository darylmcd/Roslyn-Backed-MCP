# Planning Index

<!-- purpose: Route agents to the correct in-repo planning document without mixing in-repo and cross-project scope. -->
<!-- scope: in-repo -->

## Agent contract

- **Default scope:** unnamed scope = in-repo; do not open `ai_docs/ecosystem/**` unless the user explicitly named cross-repo work. This repo currently has no local ecosystem planning set.
- **In-repo read order:** `backlog.md` first, then a named or directly relevant file under `ai_docs/plans/`.
- **Cross-project scope:** if the user names another repo, adapter, or ecosystem, say that this repo has no local cross-project planning router and work only from the external context they provided.
- **MUST:** every planning file carries a scope tag.
- **MUST:** reference-scoped planning/support files carry the reference banner.
- **MUST NOT:** duplicate roadmap or backlog content here.
- **MUST NOT:** merge in-repo and cross-project recommendations into one answer.

## Next-step protocol

1. User named no specific repo / adapter / ecosystem / integration / cross-repo term -> scope = in-repo -> read `backlog.md`, then any named in-repo file under `ai_docs/plans/` -> STOP. Do not open `ai_docs/ecosystem/**`.
2. User named another repo / adapter / ecosystem / integration / cross-repo work -> scope = cross-project -> this repo has no local `ai_docs/ecosystem/**`; say so explicitly and use only the external context the user named.
3. Both scopes named -> answer each as a separate question; do not merge into one recommendation.

## Routing table

| Scope | If you are answering... | Open first | Then |
|-------|--------------------------|------------|------|
| `in-repo` | "What should I work on next in this repo?" or any unnamed next-step question | `backlog.md` | Relevant file under `ai_docs/plans/` if a row or initiative names it |
| `cross-project` | User explicitly named another repo, adapter, ecosystem, or integration | No repo-local cross-project planner in this repo | Work only from the named external context; do not infer a local ecosystem file |
| `reference` | You need planning context, not an actionable next step | `../docs/roadmap.md` or the relevant planning-support file | Return to `backlog.md` or the named plan before recommending work |

## Maintenance

- Update this file whenever `ai_docs/backlog.md`, the active plan set, or planning-scope rules change.
- Keep this file router-only: short tables, no duplicated plan content.
