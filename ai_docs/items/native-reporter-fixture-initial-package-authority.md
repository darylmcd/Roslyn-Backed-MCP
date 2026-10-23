# native-reporter-fixture-initial-package-authority — Restore the actual native reporter fixture before loading compiler authority

**row:** `native-reporter-fixture-initial-package-authority` · **pri:** `Medium` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/NativeMtpReporterRegistrationTests.cs:86`: loads a new PackageReference project without a preceding restore.
- `tests/RoslynMcp.Tests/NativeMtpExecutionTests.cs:90`: existing actual initial-restore comparison pattern.

## Acceptance

- [ ] Perform a bounded real initial restore with the same explicit owned native globals and fixture root before the reporter workspace loads.
- [ ] Assert actual Certified native package authority and the reporter registration/output premise before each intended verdict.
- [ ] Preserve every actual success, handshake and direct-host assertion; no synthetic assets or fake certificate.
- [ ] Protect both Network fixture setup owners and preserve primary failures through cleanup.
- [ ] Explicitly run the actual current Network framework controls; ordinary ci excludes them and cannot establish their success.

## Evidence

- Cold review read both complete classes and rederived that the reporter fixture reaches InvalidRestoreAssets before its intended registration checks.
- Source reachability finding only; no runtime reproduction or reporter-product regression is claimed yet.
