# analyzer-shadow-test-age-helper-consolidation — FromDays(2)+SetCreationTimeUtc aging idiom copied across analyzer shadow tests

**row:** `analyzer-shadow-test-age-helper-consolidation` · **pri:** `Low` · **size:** `S`

## Anchors

- `tests/RoslynMcp.Tests/TestInfrastructure/ForeignHostTestSupport.cs`

## Acceptance

- [ ] One age helper used by AnalyzerShadowLoaderLifecycleTests, CreatorOwnershipTests, LeaseNoFollowTests, StartupSweepRegressionTests, SweepPathAuthorityTests.

## Evidence

- AnalyzerShadowLoaderLifecycleTests.cs:93,372,417; CreatorOwnershipTests.cs:207; LeaseNoFollowTests.cs:67; StartupSweepRegressionTests.cs:72; SweepPathAuthorityTests.cs:31.
