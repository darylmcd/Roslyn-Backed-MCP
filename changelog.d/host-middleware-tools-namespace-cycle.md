### Maintenance

- Documented the accepted `Host.Stdio.Middleware` ↔ `Host.Stdio.Tools` namespace cycle in `ai_docs/architecture.md` § *Known Gaps*. The cycle is metadata-only (middleware reads tool attributes; tools declare middleware-relevant annotations) and ships in a single assembly — no behavioral dependency, no feature blocked. The note records the trigger that would force the envelope refactor: a new middleware-driven tool *category*. Closes the `host-middleware-tools-namespace-cycle` backlog row (path-a resolution).
