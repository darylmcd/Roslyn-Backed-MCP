# feature-provider-constructor-cancellation-unwrapping

**row:** `feature-provider-constructor-cancellation-unwrapping` · **pri:** `Low` · **size:** `S`

## Anchors

- src/RoslynMcp.Roslyn/Services/CSharpFeatureProviderLoader.cs
- tests/RoslynMcp.Tests/CSharpFeatureProviderLoaderTests.cs

## Acceptance

- [ ] Unwrap TargetInvocationException when its constructor cause is OperationCanceledException and propagate cancellation instead of recording ConstructorFailure.
- [ ] Ensure the outer assembly factory boundary also preserves that cancellation.
- [ ] Pin reflected-constructor cancellation and unchanged ordinary-constructor failure reporting without raw exception logging.

## Evidence

Both catch filters exclude only a direct OperationCanceledException. Activator.CreateInstance wraps constructor exceptions, so constructor cancellation is currently counted as failure. Observed during 2026-09-15 review.
