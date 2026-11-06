---
category: Fixed
---

- **Fixed:** Persisted composite tokens (`ROSLYNMCP_PREVIEW_PERSIST_DIR`) can be redeemed by another host with the same solution loaded; a host without exactly one matching session, or a record from a newer format, refuses without consuming the token (ADR 0032). Migration: none, v1 records redeem where the creator session is live. Closes `composite-token-portable-workspace-identity`.
