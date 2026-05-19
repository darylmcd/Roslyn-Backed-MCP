---
category: Fixed
---

- **Fixed:** `get_completions` in-scope member ranking at member-access positions — added optional `triggerCharacter` parameter that, when set to `'.'`, passes `CompletionTrigger.CreateInsertionTrigger('.')` to Roslyn so method-tier candidates (locals, parameters, members) are included in the result and ranked before namespace-qualified external types. Without a trigger Roslyn returns only the position's general accessible-type set, so the existing `InScopeRank` sort had no method-tier candidates to promote. Tool description updated to document the member-access requirement. Fixes gh #768 §13.14.
