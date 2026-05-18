---
category: Fixed
---

- **Fixed:** Preview token rejection after workspace auto-reload now returns a structured `PreviewTokenStale` error category instead of a generic `NotFound`, giving callers a machine-readable signal to re-issue the paired `*_preview` call (gh #767).
