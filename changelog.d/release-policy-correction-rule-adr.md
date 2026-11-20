---
category: Changed
---

- **Changed:** Release policy (ADR 0041): a stable-tool fix that keeps response shape and error categories and only corrects a wrong result ships in a minor as `Fixed`, and removing caller input, paths or filter text exposed by a stable response or documented log event may ship in a minor with an ADR and migration note; new or changed shape, error category or message text stays major. Migration: do not read leaked fields or message text; branch on `category`. Closes `release-policy-correction-rule-adr`.
