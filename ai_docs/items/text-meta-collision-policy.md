# text-meta-collision-policy — Preserve producer text metadata on decoration

**row:** `text-meta-collision-policy` · **pri:** `Low` · **size:** `S` · **deps:** `meta-projection-failure-observability`

## Anchors

- `src/RoslynMcp.Host.Stdio/Tools/ToolErrorHandler.cs` — `InjectMetaIfPossible`.
- `tests/RoslynMcp.Tests/StructuredCallContentProjectorTests.cs`

## Acceptance

- [ ] Preserve the current text `_meta` shape when the producer supplies no `_meta` property.
- [ ] When producer metadata exists, retain it losslessly and place RoslynMcp gate metrics under a documented reserved nested member rather than overwriting either owner.
- [ ] Reject or pass through a non-object collision without data loss and emit only secret-safe diagnostics.
- [ ] One object/non-object collision matrix proves producer values survive and structured content remains unchanged.

## Evidence

- `ToolErrorHandler.InjectMetaIfPossible` unconditionally assigns `obj["_meta"]`, silently deleting a producer-owned text property and allowing the text/structured channels to diverge.
