# test-change-signature-class-granularity — Split the signature suite into independently schedulable behavior classes

**row:** `test-change-signature-class-granularity` · **pri:** `Medium` · **size:** `L` · **deps:** `test-preview-store-cross-class-eviction`

## Anchors

- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.cs:19`
- `tests/RoslynMcp.Tests/ChangeSignaturePreviewTests.StructConstruction.cs:8`
- `tests/RoslynMcp.Tests/AssemblyInfo.cs:10`
- `eng/get-test-shard-plan.ps1`

## Acceptance

- [ ] Inventory all 333 current expanded cases and partition by semantic concern; preserve every input, assertion and runtime semantic proof.
- [ ] Use distinct TestClass types, not partial files of the same class; share helpers without inherited duplicate discovery. Split implementation into independently correct batches before execution.
- [ ] Give concurrent classes explicit workspace and preview-store ownership; prove tokens cannot be displaced by sibling classes before removing the existing single-class scheduling boundary.
- [ ] Compare discovered cases before/after and verify exact class-shard union/disjointness, repeated Windows/Linux tests and full CI; measure class spans and critical path without predicting additive savings.

## Evidence

- Latest Windows class total 854.3s inside a 971.7s test leg; 333 cases. Three full Windows runs: 854.3-1281.2s. Files match latest sampled PR head.
- Source audit: `ai_docs/reports/20261017-test-efficiency/report.md`; source head `000a1ce0f87ccacd3dad9f0d0be62b72dbc08db7`.
