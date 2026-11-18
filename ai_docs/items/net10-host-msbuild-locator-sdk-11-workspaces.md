# net10-host-msbuild-locator-sdk-11-workspaces — Prove or fix workspace loading against the .NET 11 SDK from the net10.0 host

**row:** `net10-host-msbuild-locator-sdk-11-workspaces` · **pri:** `Medium` · **size:** `M` · **verified:** `14f4831243de873c2ca917e4edc954dcdbea0edb`

## Anchors

- `src/RoslynMcp.Roslyn/Helpers/DotnetCommandRunner.cs:56-62,224-233` — the three MSBuildLocator-exported variables and why child `dotnet` commands strip them.
- `Directory.Build.props:3` — `TargetFramework` `net10.0`.
- `global.json` — SDK floor (`11.0.100` once pull request 2153 merges).

## Acceptance

- [ ] Record, with a test or a captured run, which SDK `MSBuildLocator` registers in a `net10.0` process when both a 10.0.x and an 11.0.x SDK are installed, and when only an 11.0.x SDK is installed.
- [ ] `workspace_load` of a project targeting `net11.0` is exercised from the `net10.0` server: it either loads with correct semantic results or fails with an actionable error that names the cause.
- [ ] `workspace_load` on a machine with only the .NET 11 SDK (plus the .NET 10 runtime) is exercised the same way.
- [ ] If either case fails, decide in an ADR between moving the host target framework and another supported route, and state the release-version consequence.

## Evidence

- 2026-11-18, branch `build/dotnet-11-sdk-roslyn-5-12`: a `net10.0` test host with `global.json` at `11.0.100` and SDKs `10.0.404` and `11.0.100` installed exported an `MSBUILD_EXE_PATH` under `sdk\10.0.404`. The formatter generator's `dotnet restore` child then started 80 worker nodes with the command line `dotnet.exe "C:\Program Files\dotnet\sdk\10.0.404\MSBuild.dll" /noautoresponse /nologo /nodemode:1 /nodeReuse:false /low:false` and never completed.
- The formatter scripts were fixed in that branch by stripping the three variables. That fix does not answer which MSBuild the server itself evaluates user projects with.

## Context

- Not verified: whether the registration came from `MSBuildLocator` filtering SDKs newer than the running runtime, and whether the out-of-process Roslyn BuildHost is affected the same way.
- Roslyn 5.12 parses newer language versions, but project evaluation by an SDK 10 MSBuild may still reject a `net11.0` target framework.
