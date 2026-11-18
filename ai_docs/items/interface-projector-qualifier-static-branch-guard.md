# interface-projector-qualifier-static-branch-guard — Guard the ScopeQualifier static-member branch

**row:** `interface-projector-qualifier-static-branch-guard` · **pri:** `Low` · **size:** `S` · **verified:** `12bc2c8b5395056e11d2772b593347500033e5b6`

## Anchors

- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs`:528 — static field/property case in `Qualify`.
- `src/RoslynMcp.Roslyn/Services/InterfaceMemberProjector.cs`:486-499 — `RecordQualifiedName` guard covers `int.MaxValue`, not `System.Int32.MaxValue`.

## Acceptance

- [ ] The static branch checks the same default/attribute-argument context as the other branches, so a new caller cannot qualify a name outside those positions.
- [ ] `System.Int32.MaxValue` in a default records no namespace; unit test in `InterfaceMemberProjectorTests` for both.

## Evidence

- iface-projector report (PR 2147), sketches 3-4: "ScopeQualifier.Qualify static field/property branch has no default/attribute-argument guard (works because callers pre-filter)"; "RecordQualifiedName predefined-type guard covers one access level only".
- Verified at 12bc2c8b: lines 489 and 528.
