---
category: Maintenance
---

- **Maintenance:** The `publish-nuget` workflow now authenticates to nuget.org with GitHub OIDC trusted publishing (`NuGet/login`, pinned to a commit SHA) instead of the long-lived `NUGET_API_KEY` repository secret, which expired and failed the v5.0.1 publish with a 403. The `publish` job gains `id-token: write`; a missing or mismatched nuget.org trusted-publishing policy now fails the push step with a message naming the policy. The manual `eng/publish-nuget.ps1` path keeps reading `NUGET_API_KEY` from the environment.
