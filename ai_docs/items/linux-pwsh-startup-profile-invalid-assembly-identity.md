# linux-pwsh-startup-profile-invalid-assembly-identity — Diagnose native startup identity corruption

**row:** `linux-pwsh-startup-profile-invalid-assembly-identity` · **pri:** `High` · **size:** `S`

## Anchors

- .github/workflows/ci.yml (Linux validation process tree)
- tests/RoslynMcp.Tests/CiTopologyDecisionContractTests.cs (first observed failing child)
- tests/RoslynMcp.Tests/Helpers/PwshScriptRunner.cs (shared launcher; not the only native child owner)

## Acceptance

- [ ] Preserve run40373606428 / Linux1 job121750682497 / head2374a09b4cc997720c6917ffc6f6d4ccda7fd1ca native134 evidence, including malformed assembly identity and first failure before cancellation.
- [ ] Obtain a bounded causal playback witness using retained cache/native evidence or a specimen proven to enter the failing assembly-load branch; observe red before a repair is called fixed. A green isolated or synthetic control is non-reproduction only.
- [ ] Repair the proven owner without internal runtime switches, diagnostic suppression, cache-clear retries, test skips, assertion/deadline changes or quarantine. If an upstream runtime change is required, retain the external blocker and exact compatible release evidence.
- [ ] Qualify every affected Linux child/descendant launch and all required hosted checks. Treat cache immutability as a separate candidate requiring module-analysis/telemetry write compatibility proof; do not claim whole-directory freezing is a demonstrated cure.

## Evidence

- First failure SameInputs_ProduceByteIdenticalJsonAsync: pwsh134, System.IO.FileLoadException, System.Collections.Specialized Version10.0.0.0 public-key token b03f5f7f11d50a3 (missing final a); failures recur in existing and new subprocess tests, followed by30-minute cancellation. Five other matrix legs, quality, SDKfloor and CodeQL pass; overall required gate fails.
- Upstream open dotnet/runtime issue121977 concerns startup-profile concurrent write corruption; PowerShell28119 tentatively connects parallel7.6.6/Linux crashes. Similarity is an inference, not a proven shared root cause with this witness or separate bare139.
- Native ext4, nonroot, portable PowerShell7.6.6/.NET10.0.12 experiment: fresh public-API seed,8 concurrent functional launches plus descendant pass, profile hash unchanged under444/555permissions. Single synthetic structurally validated truncated identity also exits0: causal reproduction FAILED. Fresh cold plan BLOCKED this as a complete CI134 fix because playback branch was not proven and whole powershell directory freeze affects other writes.
- Scratch report powershell-profile-cache-proof.md and55-artifact hash manifest retain commands/results; no product or CI runtime change was authored. PR1849 remains draft/frozen. See ai_docs/reports/2026-10-26-interface-split-delivery.md.
