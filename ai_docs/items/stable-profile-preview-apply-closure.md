# stable-profile-preview-apply-closure — Keep preview/apply routes closed under tier selection

**row:** `stable-profile-preview-apply-closure` · **pri:** `Medium` · **size:** `M`

## Anchors

- `src/RoslynMcp.Host.Stdio/Catalog/SurfaceRegistrationPolicy.cs`
- `src/RoslynMcp.Host.Stdio/ServerInstructions.cs`
- `src/RoslynMcp.Host.Stdio/Catalog/ServerSurfaceCatalog.cs`
- `tests/RoslynMcp.Tests/StartupDiagnosticsTests.cs`
- `tests/RoslynMcp.Tests/ServerDiscoveryWireTests.cs`

## Acceptance

- [ ] Every selected preview tool has a selected compatible apply route, or is omitted or explicitly marked analysis-only.
- [ ] Stable instructions, catalog resources, and workflow hints name only selected and callable endpoints.
- [ ] Preserve the default all-tier surface byte-for-byte.
- [ ] Add a stable-profile wire regression proving tokens cannot be issued for a filtered apply route and filtered routes remain unreachable.

## Evidence

- Several stable preview tools retain experimental apply partners, so stable discovery can currently issue a token that has no advertised or callable redemption route.
