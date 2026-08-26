---
category: Maintenance
---

- **Maintenance:** Parameter-description dedupe — validation/signature cluster (`param-description-dedupe-validation-signature`): standardized the `workspaceId`, required-path, and `previewToken` boilerplate across `validate_workspace`, `validate_recent_git_changes`, `workspace_fork_apply`, `move_type_to_project_preview`, `extract_interface_cross_project_preview`, `dependency_inversion_preview`, `change_signature_preview`, and `parameter_object_preview` onto the canonical one-liners, and dropped duplicated version-archaeology text from the two `summary` parameters. Discriminating guidance (`op`/`newOrder`/`retention`/`metadataName` contracts, the `native JSON array` encoding hints, and the fork tool's "source workspace" qualifier) is unchanged. `ParameterDescriptionCanonicalizationTests` now ratchets these four tool types so the phrasing cannot drift back.
