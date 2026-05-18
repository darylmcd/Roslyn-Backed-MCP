---
category: Fixed
---

- **Fixed:** `symbol_relationships` returning a 57+ KB payload overflow when `preferDeclaringMember=false` and the cursor lands on a builtin-type token (e.g. `void`, `int`). The tool now detects builtin-type resolution (`SpecialType != None`) and returns an empty relationship envelope with a `hint` field explaining the suppression, rather than enumerating all solution-wide references to the builtin. The `preferDeclaringMember=true` auto-promotion path is unaffected. Closes gh #757.
