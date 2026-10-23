# native-buildhost-escaped-argument-transport — Preserve escaped native command items across the owned build host

**row:** `native-buildhost-escaped-argument-transport` · **pri:** `High` · **size:** `L`

## Anchors

- `src/RoslynMcp.Roslyn/RoslynMcp.Roslyn.csproj`: vendor BuildHost content provisioning.
- `src/RoslynMcp.Host.Stdio/RoslynMcp.Host.Stdio.csproj`: shipped host and tool-package closure.
- `Directory.Packages.props`: pinned Roslyn/MSBuild dependencies.
- `tests/RoslynMcp.Tests/NativeWorkspaceArgumentTransportTests.cs`: real parent/child RPC and semantic AssemblyName/ModuleName controls.
- `tests/RoslynMcp.Tests/NativeCscArgumentTransportTests.cs`: independent native command producer and compiler grammar.

## Acceptance

- [ ] Preserve the escaped representation of native Csc and Vbc command items exactly once across the real BuildHost RPC boundary.
- [ ] Pin and retain upstream source/license provenance and every owned correction; reject unsupported task-item representations without lossy fallback.
- [ ] Provision both native host variants through actual build dependencies and complete output items on clean builds, publish, test deployment and dotnet-tool packaging.
- [ ] Preserve original native compiler/reference/options behavior; no authored-target rewrite, source-specific patch, version suppression or fallback to lossy vendor host.
- [ ] Verify actual selected child identity and complete copied artifact hashes.
- [ ] Observe native percent-literal failures on the original reader, then all cases passing through actual owned-child RPC; qualify Windows and Linux SDK-floor/runtime paths.
- [ ] Finish clean-build/publish/pack and full final merged-main validation before declaring delivery.

## Evidence

- Immutable causal19 actual vendor parent/child controls: five percent cases fail semantic compilation-name comparison; plain, spaces and literal semicolon controls pass.
- Pinned upstream source 35d9211b841e7613c1d2f8f5af6d628ace696c4c reads ProjectInstance command ItemSpec, which is already unescaped.
- Scratch owned host with original reader and explicit Contracts null guard builds net8.0/net472 with zero warnings/errors, reproduces 5 failed/3 passed actual RPC controls in 17s.
- Scratch owned host using required ITaskItem2.EvaluatedIncludeEscaped builds both variants with zero warnings/errors; the same actual parent/child RPC controls pass 8/8 in 17s.
- Actual copied signed parent 5.9 launcher resolves the owned unsigned 5.9 sibling child; exact source/output SHA256 manifests retained under scratch owned-buildhost-probe.
- This is scratch qualification only: repository deployment, Linux/floor proof, native framework execution, packaging and final gate remain undone.

2026-10-23 owned import audit additionally confirms pinned NetFrameworkBuildHost.cs:101-104 FindMSBuild throws NotImplementedException. Existing HasUsable route currently avoids it; no observed runtime defect claim. Required owned import disposition: implement supported real locator/RPC behavior or establish exact protocol path unreachability and remove dead method with real parent compatibility proof. Do not silently import latent throw, CA1822 suppressions or untracked TODOs. Classic bounded actual Framework route probe cold source-reviewed; baseline build queued, runtime unqualified.

2026-10-23 Classic baseline probe build PASS0warnings/errors2.01s. Matrix exit1 BEFOREany8nativecase: Windows Desktop PowerShell refused select-classic-msbuild.ps1 because running scripts is disabled (UnauthorizedAccess). No Framework/percent causal result. Do not use ExecutionPolicyBypass/rewritten -Command/disableguard. Investigate existing permitted vswhere native inventory with exact actualchild MSBuildBinPath equality and cold selection proof; otherwise report classic qualification blocked. Netcore Win8P/Linuxfloor8P remain valid separate scratch proofs; no repo deployment.

Classic Framework qualification now genuine: independent native Full runtime, exact highest VS18.11 Bin/amd64 executable/hash, VS Roslyn target receipt and actual PE premises passed all8. Unpatched owned child baseline5failed3passed: literal percent encodings lost identity in actual RPC and semantic consumer despite correct native PE. Fixed owned child8passed0failed, nonpath backslash feature preserved; signed parent hash2987CF1F unchanged, actual Framework4.8.10008 child fixedSHA6FA7CA4179BE8E1A5E0840C1B1052F0BDDA9C12AB0DFDBFD77815B6E32835EE9. Raw classic-native-baseline-environment-results and classic-native-fixed-environment-results plus qualified-summary retained. Corrected independent producer removes only MSBUILD_EXE_PATH like vendor launcher; original probe inherited Core bootstrap and failed before native PE, so those earlier failures were not semantic proof. Native core Windows/Linux already qualified separately; repository integration, clean both-TFM builds, transitive test/publish/PackAsTool and missing-child failures remain unqualified.

Fresh cold review corrected integration design: preserve net8/net472 for alternate SDK dotnet roots; single maintained corrected source with original hashes/diffs; separate read-only closure discovery from conditional build; real remoting contract without dummy token state; aligned test-only VB language services. Dependency-only exact proposed graph with scoped MSBuild17.11.48, explicit Xml/Pkcs10.0.13 and private Framework umbrella1.0.3 restored successfully with all advisory-source/vulnerability warnings as errors; per-project include-transitive audit reports no vulnerable packages. Scratch proof only, not source integration/deployment or cross-OS graph equivalence. Approved plan and manifests in <user-profile>/.codex/scratch/roslyn-remediate-20261023/.
