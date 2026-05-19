---
category: Fixed
---

- **Fixed:** `suggest_refactorings` false-positive on facade/adapter types — zero-instance-field types implementing one or more interfaces with all-delegating public methods (expression-bodied or single-return) no longer surface a top-severity "Split" cohesion recommendation. `CohesionAnalysisService` now detects the `"facade"` lifecycle pattern alongside `"action-triad"`, and `RefactoringSuggestionService` suppresses cohesion suggestions for any type bearing a `LifecyclePattern` value. Fixes gh #768 §13.10.
