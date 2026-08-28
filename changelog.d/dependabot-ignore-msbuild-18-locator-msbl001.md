---
category: Maintenance
---

- **Maintenance:** Dependabot no longer proposes MSBuild 18.x. Bumping the `msbuild-compile-family` group to 18.x pulls `Microsoft.NET.StringTools` 18.x transitively, which fails the build with `MSBL001` under `Microsoft.Build.Locator` 1.11.2 because that package carries no `ExcludeAssets="runtime" PrivateAssets="all"` of its own. `.github/dependabot.yml` now ignores `>=18.0.0` for the four `Microsoft.Build.*` pins, with the two conditions required to lift it recorded inline. Closes PR #1330.
