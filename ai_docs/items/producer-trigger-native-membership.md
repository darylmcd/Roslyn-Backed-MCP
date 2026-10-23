# producer-trigger-native-membership — producer-trigger-native-membership

**row:** `producer-trigger-native-membership` · **pri:** `Medium` · **size:** `S`

## Anchors
- `tests/RoslynMcp.Tests/NativeCompilerContextCertificateTests.cs`: `CompilerProducer_RejectsAuthoredInputChangedDuringAdditionalNativeExecution`, `CreateAsync`.

## Acceptance
- Trigger native producer mutation without adding an evaluated authored/default item after certification.
- Prove native membership is unchanged by the trigger; preserve both actual source/artifact mutation assertions and exact physical-input refusal.
- Observe both original cases fail at premature membership validation, then both corrected cases pass.

## Evidence
- Root marker was enrolled by SDK default None glob. Existing owned obj directory excludes the execution-only marker.
- Targeted run `producer-trigger-membership-proposal/targeted-73ad643a42cf4b17a5d7d7129642ea68`: baseline2FAIL for native membership change; corrected2PASS10.132s/7.282s. Correction installed.
- Full shipping qualification is pending the other four original failures and operator decisions; do not infer broad verification from this targeted receipt.
