# watcher-delivery-loss-observation — Fail conservatively when native watcher delivery is lost

**row:** `watcher-delivery-loss-observation` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Roslyn/Services/FileWatcherService.cs:219`: CreateWatcher enables native events before subscribing handlers and omits the Error event.
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:35`: stale observation retries rewrite the source after timeouts.
- `tests/RoslynMcp.Tests/ExternalEditStalenessTests.cs:582`: retry helper treats dropped events as flakes without proving the original event was observed.

## Acceptance

- [ ] Subscribe all native change and Error handlers before enabling event delivery.
- [ ] Treat watcher delivery failure or overflow as unknown freshness; conservatively mark affected workspaces stale or perform an authoritative rescan.
- [ ] Preserve exact workspace ownership and configuration-write attribution.
- [ ] Replace source rewrite retries with a single actual mutation and deterministic enrollment/observation evidence.
- [ ] Exercise initial enrollment and an actual delivered watcher-error path without claiming an unobserved overflow reproduction.

## Evidence

- Cold review read the actual CreateWatcher event ordering and complete stale-observation helper in this session.
- This is a source-derived delivery-loss contract gap, separate from owned EditorConfig attribution; no native overflow runtime reproduction is claimed.
