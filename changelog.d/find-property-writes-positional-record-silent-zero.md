---
category: Fixed
---

- **Fixed:** `find_property_writes` now reports positional-record primary-constructor bindings via a new `PrimaryConstructorBind` `WriteKind` bucket — construction-site positional args at `new T(arg0, …)` / base-record `R : Base(…)` slots are attributed back to the synthesized property (`MutationAnalysisService`, `SymbolTools`). `PropertyWriteDto.IsObjectInitializer` (bool) is replaced by a `WriteKind` string discriminator (`Assignment` / `ObjectInitializer` / `OutRef` / `PrimaryConstructorBind`) — breaking DTO-shape change on the internal caller surface (`find-property-writes-positional-record-silent-zero`).
