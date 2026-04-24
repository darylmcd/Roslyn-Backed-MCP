---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `server_info.connection.state` and `server_heartbeat.connection.state` now report `idle` before any workspace is loaded (previously the unsatisfiable `initializing`, which implied auto-advance but required a `workspace_load` to transition). Prompts gating on `state==ready` to mean "server responsive" should gate on `state in {idle, ready}` (`ServerTools`). (`connection-state-ready-unsatisfiable-preload`)
