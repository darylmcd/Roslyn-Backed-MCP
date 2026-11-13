---
category: Fixed
---

- **Fixed:** Child `dotnet` commands (`build`, `restore`, `msbuild`, `test`) no longer inherit `MSBUILD_EXE_PATH`, `MSBuildExtensionsPath` and `MSBuildSDKsPath` from the server process. MSBuildLocator exports those variables for the SDK the server selected; a child that resolved a different SDK from its working directory's `global.json` (for example SDK 11.0.100 beside 10.0.404 on the ubuntu-26.04 hosted image) launched worker nodes from the pinned SDK, every node handshake failed (`Received 12 from host for FileVersionMinor but expected 9`) and the command ended `Build FAILED` with `0 Error(s)` and exit code 1.
