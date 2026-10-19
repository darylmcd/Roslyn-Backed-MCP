---
category: Fixed
---

- **Fixed:** Cohesion analysis includes handwritten partial implementations once, connects real method dependencies while excluding `nameof` operands, recognizes explicit generic shared-helper calls, and ranks results before limiting. Parameter-count suggestions discover simple methods, share preview target eligibility, and recommend manual changes for unsupported or ambiguous targets. Closes `cohesion-handwritten-partial-method-inclusion`, `cohesion-direct-method-call-connectivity`, `shared-members-explicit-generic-call-traversal`, `cohesion-ranked-limit-after-discovery`, and `suggestion-parameter-object-tool-guidance`.
