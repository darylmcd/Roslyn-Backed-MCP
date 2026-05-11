### Fixed

- `apply_project_mutation` now registers on the `revert_last_apply` stack — pipelines relying on revert-stack rollback no longer leak `.csproj` mutations. ([#640](https://github.com/darylmcd/Roslyn-Backed-MCP/pull/640))
