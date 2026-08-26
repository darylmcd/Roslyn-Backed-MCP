---
category: Changed — BREAKING
---

- **Changed — BREAKING:** `bulk_replace_type_apply` and `remove_dead_code_apply` now refuse a preview token minted by a different producer family, before any workspace mutation, instead of applying it. A caller that redeemed a foreign token through either route — which previously succeeded — now receives an error naming the token's real producer and the route that should have been used.
