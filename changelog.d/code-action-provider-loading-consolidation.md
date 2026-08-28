---
category: Fixed
---

- **Fixed:** provider discovery now uses one typed, secret-safe failure model across code actions, registry queries, and FixAll; malformed FixAll scope input is validated before provider lookup; replace-invocation previews advertise and catalog their shared apply route; and the related workspace fixture closes without masking primary failures. Closes `code-action-provider-loading-consolidation`, `fixall-scope-required-validation-hoist`, `fixalltools-projectname-stale-optional-description`, `replace-invocation-preview-apply-route-undocumented`, and `bulk-refactoring-test-workspace-leak-and-unguarded-cleanup`.
