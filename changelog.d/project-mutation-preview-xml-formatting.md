---
category: Fixed
---

- **Fixed:** Project-mutation preview tools (`add_package_reference_preview`, `set_project_property_preview`, `add_central_package_version_preview`) now emit formatted XML with proper line breaks between groups instead of collapsed single-line output. Root cause was `ProjectMutationService` holding pre-FORMAT-BUG-003 duplicates of `GetOrCreateItemGroup`/`AddChildElementPreservingIndentation`/`RemoveElementCleanly`/`FormatProjectXml`; consolidated onto `OrchestrationMsBuildXml` and rewrote `SetElementValue` call-sites to route new-child inserts through trivia-aware splice helpers. Same fix incidentally repairs `set_conditional_property_preview`, `add_target_framework_preview`, `remove_target_framework_preview`, and `add_project_reference_preview` (`project-mutation-preview-xml-formatting`).
