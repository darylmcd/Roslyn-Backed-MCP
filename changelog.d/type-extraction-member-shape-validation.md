---
category: Fixed
---

- **Fixed:** `extract_type` preview now refuses unsafe extraction member shapes — a source-type constructor named in `memberNames` is refused instead of being emitted unchanged into the new type, a multi-declarator field is split so only the requested variables move (unrequested siblings are never silently dragged along), and an ambiguous method-overload name is refused with the candidate signatures listed instead of silently picking the first-declared overload by source order.
