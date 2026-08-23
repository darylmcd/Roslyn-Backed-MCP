# Third-Party Notices

Roslyn-Backed MCP Server uses the following open-source packages. Versions come from `Directory.Packages.props`; license and project fields are reviewed attribution metadata maintained by `eng/update-third-party-notices.ps1`.

## Runtime Dependencies

| Package | Version | License | Project |
|---|---:|---|---|
| DiffPlex | 1.9.0 | Apache-2.0 | https://github.com/mmanela/diffplex |
| Microsoft.Build.Locator | 1.11.2 | MIT | https://github.com/microsoft/MSBuildLocator |
| Microsoft.CodeAnalysis.CSharp | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.CSharp.Features | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.CSharp.Scripting | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.CSharp.Workspaces | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.Features | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.CodeAnalysis.Workspaces.MSBuild | 5.6.0 | MIT | https://github.com/dotnet/roslyn |
| Microsoft.Extensions.Hosting | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Http | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| Microsoft.Extensions.Logging.Console | 10.0.10 | MIT | https://github.com/dotnet/runtime |
| ModelContextProtocol | 2.1.0 | MIT | https://github.com/modelcontextprotocol/csharp-sdk |
| Nito.AsyncEx | 5.1.2 | MIT | https://github.com/StephenCleary/AsyncEx |
| System.Security.Cryptography.Xml | 10.0.10 | MIT | https://github.com/dotnet/runtime |

## Build-Time Dependencies

| Package | Version | License | Project |
|---|---:|---|---|
| Microsoft.Build | 17.14.28 | MIT | https://github.com/dotnet/msbuild |
| Microsoft.Build.Framework | 17.14.28 | MIT | https://github.com/dotnet/msbuild |
| Microsoft.Build.Tasks.Core | 17.14.28 | MIT | https://github.com/dotnet/msbuild |
| Microsoft.Build.Utilities.Core | 17.14.28 | MIT | https://github.com/dotnet/msbuild |
| Microsoft.CodeAnalysis.Analyzers | 5.6.0 | MIT | https://github.com/dotnet/roslyn-analyzers |
| Microsoft.CodeAnalysis.BannedApiAnalyzers | 5.6.0 | MIT | https://github.com/dotnet/roslyn-analyzers |
| Microsoft.CodeAnalysis.NetAnalyzers | 10.0.302 | MIT | https://github.com/dotnet/sdk |
| Microsoft.SourceLink.GitHub | 10.0.301 | MIT | https://github.com/dotnet/sourcelink |

## Test Dependencies

| Package | Version | License | Project |
|---|---:|---|---|
| coverlet.collector | 10.0.1 | MIT | https://github.com/coverlet-coverage/coverlet |
| Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.MSTest | 1.1.2 | MIT | https://github.com/dotnet/roslyn-sdk |
| Microsoft.Extensions.TimeProvider.Testing | 10.8.0 | MIT | https://github.com/dotnet/extensions |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT | https://github.com/microsoft/vstest |
| MSTest.TestAdapter | 4.3.3 | MIT | https://github.com/microsoft/testfx |
| MSTest.TestFramework | 4.3.3 | MIT | https://github.com/microsoft/testfx |
| NuGet.Frameworks | 6.3.4 | Apache-2.0 | https://github.com/NuGet/NuGet.Client |

---

Run `pwsh eng/update-third-party-notices.ps1` after changing central package pins. Verification fails closed when a package lacks reviewed attribution metadata.
