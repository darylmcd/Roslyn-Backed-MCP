---
category: Maintenance
---

- **Maintenance:** `publish-nuget.yml` gains an opt-in `publish_registry` `workflow_dispatch` input so the MCP Registry job can run from `main` without a tag (version read from `Directory.Build.props`); tag and release triggers are unchanged. Recovery path for a release whose tag-triggered run could not publish; first used for 5.0.1.
