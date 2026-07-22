---
category: Maintenance
---

- **Maintenance:** extracted `StructuredCallElicitationCoordinator` and `StructuredCallContentProjector` from `StructuredCallToolFilter`, completing the hotspot-decomposition follow-up — elicitation/retry orchestration and structured-content/`_meta` projection now live in focused, directly-tested collaborators, and `StructuredCallToolFilter.Create` is orchestration-only (filter file 963 → 544 lines). The historical static call surface is preserved via thin forwarding delegates, so existing callers and test suites are unchanged. Closes `structuredcalltoolfilter-hotspot-decomposition-followup`.
