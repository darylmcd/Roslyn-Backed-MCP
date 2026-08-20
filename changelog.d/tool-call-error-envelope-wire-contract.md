---
category: Fixed
---

- **Fixed:** `tools/call` failure envelopes are now protocol-era shaped like success envelopes — legacy (`2025-11-25`) sessions no longer receive the July-2026-only `resultType: "complete"` discriminator on error frames. A new raw JSON-RPC regression suite pins the whole failure contract: an unexpected nested exception provably serializes as a JSON-RPC `result` with `isError: true` and one redacted application-error text block (never a protocol `error` member), carries application `_meta` without `schemaHint`, never promotes those fields onto the protocol result `_meta`, and leaks no secret payload, exception type, inner message, stack frame, or local path across both supported protocol eras.
