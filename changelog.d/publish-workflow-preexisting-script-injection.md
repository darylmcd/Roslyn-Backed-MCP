---
category: Fixed
---

- **Fixed:** Closed two GitHub Actions script-injection sites in `.github/workflows/publish-nuget.yml`. The `Resolve package version` step interpolated `${{ github.event.release.tag_name }}`, and the nuget.org readme poll interpolated `${{ steps.ver.outputs.version }}` (derived from that same tag), directly into `run:` bodies in jobs holding `NUGET_API_KEY`. Because `${{ }}` is substituted into the workflow source before any shell parses it, a release tag containing a quote could close the string and execute arbitrary commands — quoting the expansion is not a mitigation. Both now bind through `env:` and are read as `$NAME`. A new `PublishWorkflow_NeverInterpolatesContextIntoRunBodies` regression lock walks every `run:` body and fails on any `${{ }}` expression. Closes `publish-workflow-preexisting-script-injection`.
