---
category: Maintenance
---

- **Maintenance:** Added `.github/dependabot.yml` (weekly `github-actions` + `nuget` ecosystem updates) and bumped CI actions off the deprecated Node 20 runtime — `actions/checkout@v4→v7`, `actions/cache@v4→v5`, `actions/upload-artifact@v4→v7` across `ci.yml` and `publish-nuget.yml`, clearing the "Node.js 20 is deprecated" warning. Closes `repo-dependabot-config`.
