using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestShardPlanContractTests
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(30);

    [TestMethod]
    [TestCategory("Process")]
    public async Task Planner_OneTwoAndFourShards_AreCompleteDisjointBalancedAndDeterministicAsync()
    {
        var testAssemblyPath = typeof(TestShardPlanContractTests).Assembly.Location;

        var oneShardResult = await RunPlannerAsync(testAssemblyPath, shardCount: 1, shardIndex: 0);
        var zeroIndexTwoShardResult = await RunPlannerAsync(testAssemblyPath, shardCount: 2, shardIndex: 0);
        var firstMaxIndexFourShardResult = await RunPlannerAsync(testAssemblyPath, shardCount: 4, shardIndex: 3);
        var secondMaxIndexFourShardResult = await RunPlannerAsync(testAssemblyPath, shardCount: 4, shardIndex: 3);

        AssertSucceeded(oneShardResult);
        AssertSucceeded(zeroIndexTwoShardResult);
        AssertSucceeded(firstMaxIndexFourShardResult);
        AssertSucceeded(secondMaxIndexFourShardResult);
        Assert.AreEqual(
            firstMaxIndexFourShardResult.StdOut.Trim(),
            secondMaxIndexFourShardResult.StdOut.Trim(),
            "The same assembly and shard arguments must produce byte-identical JSON.");

        var oneShardPlan = ParsePlan(oneShardResult.StdOut);
        var zeroIndexTwoShardPlan = ParsePlan(zeroIndexTwoShardResult.StdOut);
        var maxIndexFourShardPlan = ParsePlan(firstMaxIndexFourShardResult.StdOut);

        Assert.AreEqual(1, oneShardPlan.SchemaVersion);
        Assert.AreEqual(1, oneShardPlan.TestShardCount);
        Assert.AreEqual(oneShardPlan.TotalClassCount, oneShardPlan.TestClasses.Length);
        Assert.IsTrue(oneShardPlan.TotalClassCount > 0);

        AssertCompleteDisjointBalancedPlan(oneShardPlan, oneShardPlan, expectedShardCount: 1);
        AssertCompleteDisjointBalancedPlan(oneShardPlan, zeroIndexTwoShardPlan, expectedShardCount: 2);
        AssertCompleteDisjointBalancedPlan(oneShardPlan, maxIndexFourShardPlan, expectedShardCount: 4);
        AssertSelectedShard(oneShardPlan, expectedIndex: 0);
        AssertSelectedShard(zeroIndexTwoShardPlan, expectedIndex: 0);
        AssertSelectedShard(maxIndexFourShardPlan, expectedIndex: 3);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Planner_HostedDurationProfile_SeparatesSlowClassAndPreservesPartitionAsync()
    {
        var assemblyPath = typeof(TestShardPlanContractTests).Assembly.Location;
        var baselineResult = await RunPlannerAsync(assemblyPath, shardCount: 1, shardIndex: 0);
        AssertSucceeded(baselineResult);
        var baseline = ParsePlan(baselineResult.StdOut);
        var slowClass = baseline.TestClasses.First(testClass => testClass.StaticCaseWeight == 1).ClassName;
        var fallbackClasses = baseline.TestClasses.Where(testClass =>
            testClass.StaticCaseWeight == 1 && testClass.ClassName != slowClass).Take(2).ToArray();
        Assert.AreEqual(2, fallbackClasses.Length);
        var omittedClass = fallbackClasses[0].ClassName;
        var changedClass = fallbackClasses[1].ClassName;
        var fixtureRoot = Path.Combine(TestTempRoot.Current, nameof(TestShardPlanContractTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var profilePath = Path.Combine(fixtureRoot, "profile.json");
        try
        {
            var profile = new
            {
                SchemaVersion = 1,
                MinimumSamples = 5,
                Images = new[]
                {
                    new
                    {
                        Image = "ubuntu-latest",
                        RunIds = new[] { "r1", "r2", "r3", "r4", "r5" },
                        Classes = baseline.TestClasses.Select(testClass => new
                        {
                            testClass.ClassName,
                            testClass.StaticCaseWeight,
                            MeanCaseSeconds = testClass.ClassName == slowClass
                                ? 100_000d : testClass.StaticCaseWeight,
                        }).ToArray(),
                    },
                },
            };
            await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile));
            var result = await RunPlannerAsync(assemblyPath, shardCount: 2, shardIndex: 0,
                durationProfilePath: profilePath, hostedImage: "ubuntu-latest");
            AssertSucceeded(result);
            var planned = ParsePlan(result.StdOut);
            Assert.AreEqual(1, planned.Shards[0].ClassCount,
                "The expensive class must occupy its own shard under measured duration weighting.");
            CollectionAssert.AreEqual(new[] { slowClass }, planned.Shards[0].Classes);
            AssertCompleteDisjointBalancedPlan(baseline, planned, expectedShardCount: 2,
                checkStaticBalance: false);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.AreEqual("hosted-duration", document.RootElement.GetProperty("WeightingMode").GetString());
            Assert.AreEqual(baseline.TotalClassCount,
                document.RootElement.GetProperty("ProfiledClassCount").GetInt32());
            Assert.AreEqual(0, document.RootElement.GetProperty("FallbackClassCount").GetInt32());

            var fallbackProfile = new
            {
                SchemaVersion = 1,
                MinimumSamples = 5,
                Images = new[]
                {
                    new
                    {
                        Image = "ubuntu-latest",
                        RunIds = new[] { "r1", "r2", "r3", "r4", "r5" },
                        Classes = baseline.TestClasses.Where(testClass => testClass.ClassName != omittedClass)
                            .Select(testClass => new
                            {
                                testClass.ClassName,
                                StaticCaseWeight = testClass.StaticCaseWeight +
                                    (testClass.ClassName == changedClass ? 1 : 0),
                                MeanCaseSeconds = testClass.ClassName == changedClass ? 1_000_000_000d :
                                    testClass.ClassName == slowClass ? 100_000d : testClass.StaticCaseWeight,
                            }).ToArray(),
                    },
                },
            };
            await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(fallbackProfile));
            var fallbackResult = await RunPlannerAsync(assemblyPath, shardCount: 2, shardIndex: 0,
                durationProfilePath: profilePath, hostedImage: "ubuntu-latest");
            AssertSucceeded(fallbackResult);
            var fallbackPlan = ParsePlan(fallbackResult.StdOut);
            AssertCompleteDisjointBalancedPlan(baseline, fallbackPlan, expectedShardCount: 2,
                checkStaticBalance: false);
            CollectionAssert.AreEqual(new[] { slowClass }, fallbackPlan.Shards[0].Classes,
                "The stale billion-second profile entry must not control placement.");
            CollectionAssert.IsSubsetOf(new[] { omittedClass, changedClass }, fallbackPlan.Shards[1].Classes);
            using var fallbackDocument = JsonDocument.Parse(fallbackResult.StdOut);
            Assert.AreEqual(2, fallbackDocument.RootElement.GetProperty("FallbackClassCount").GetInt32());
            var profiledEntries = fallbackProfile.Images[0].Classes
                .Where(entry => entry.ClassName != changedClass).ToArray();
            var secondsPerStaticCase = profiledEntries.Sum(entry => entry.MeanCaseSeconds) /
                profiledEntries.Sum(entry => entry.StaticCaseWeight);
            foreach (var className in new[] { omittedClass, changedClass })
            {
                var plannedClass = fallbackDocument.RootElement.GetProperty("TestClasses")
                    .EnumerateArray().Single(entry => entry.GetProperty("ClassName").GetString() == className);
                Assert.AreEqual(secondsPerStaticCase,
                    plannedClass.GetProperty("BalanceWeight").GetDouble(), 0.001);
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Planner_InvalidHostedProfile_FailsClosedAsync()
    {
        var assemblyPath = typeof(TestShardPlanContractTests).Assembly.Location;
        var fixtureRoot = Path.Combine(TestTempRoot.Current, nameof(TestShardPlanContractTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var profilePath = Path.Combine(fixtureRoot, "profile.json");
        try
        {
            var cases = new[]
            {
                (Json: "{invalid", Diagnostic: "Duration profile is invalid JSON"),
                (Json: JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    MinimumSamples = 5,
                    Images = new[] { new { Image = "windows-latest", RunIds = new[] { "r1", "r2", "r3", "r4", "r5" }, Classes = Array.Empty<object>() } },
                }), Diagnostic: "has no 'ubuntu-latest' image"),
                (Json: JsonSerializer.Serialize(new
                {
                    SchemaVersion = 1,
                    MinimumSamples = 5,
                    Images = new[] { new
                    {
                        Image = "ubuntu-latest",
                        RunIds = new[] { "r1" },
                        Classes = new[] { new { ClassName = "RoslynMcp.Tests.TestShardPlanContractTests", StaticCaseWeight = 1, MeanCaseSeconds = 10d } },
                    } },
                }), Diagnostic: "fewer than 5 source runs"),
            };
            foreach (var testCase in cases)
            {
                await File.WriteAllTextAsync(profilePath, testCase.Json);
                var result = await RunPlannerAsync(assemblyPath, shardCount: 2, shardIndex: 0,
                    durationProfilePath: profilePath, hostedImage: "ubuntu-latest");
                Assert.AreNotEqual(0, result.ExitCode, "Invalid hosted profile must fail closed.");
                StringAssert.Contains(result.StdOut + result.StdErr, testCase.Diagnostic);
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static void AssertCompleteDisjointBalancedPlan(
        TestShardPlan completePlan,
        TestShardPlan shardedPlan,
        int expectedShardCount,
        bool checkStaticBalance = true)
    {
        Assert.AreEqual(expectedShardCount, shardedPlan.TestShardCount);
        Assert.AreEqual(expectedShardCount, shardedPlan.Shards.Length);
        Assert.AreEqual(completePlan.TotalClassCount, shardedPlan.TotalClassCount);
        Assert.AreEqual(completePlan.TotalStaticCaseWeight, shardedPlan.TotalStaticCaseWeight);

        var expectedClasses = completePlan.TestClasses
            .Select(testClass => testClass.ClassName)
            .ToHashSet(StringComparer.Ordinal);
        var assignedClasses = new HashSet<string>(StringComparer.Ordinal);
        for (var expectedIndex = 0; expectedIndex < shardedPlan.Shards.Length; expectedIndex++)
        {
            var shard = shardedPlan.Shards[expectedIndex];
            Assert.AreEqual(expectedIndex, shard.Index);
            Assert.IsTrue(shard.ClassCount > 0, $"Shard {shard.Index} is empty.");
            Assert.AreEqual(shard.ClassCount, shard.Classes.Length);
            foreach (var className in shard.Classes)
            {
                Assert.IsTrue(
                    assignedClasses.Add(className),
                    $"Class '{className}' occurs in more than one shard.");
            }

            AssertSafeExactFilter(shard);
        }

        Assert.IsTrue(
            expectedClasses.SetEquals(assignedClasses),
            $"The {shardedPlan.TestShardCount}-shard union must equal the complete class catalog.");

        if (checkStaticBalance)
        {
            var lightestWeight = shardedPlan.Shards.Min(shard => shard.StaticCaseWeight);
            var heaviestWeight = shardedPlan.Shards.Max(shard => shard.StaticCaseWeight);
            var largestClassWeight = shardedPlan.TestClasses.Max(testClass => testClass.StaticCaseWeight);
            Assert.IsTrue(
                heaviestWeight - lightestWeight <= largestClassWeight,
                $"Greedy balance regressed: shard delta {heaviestWeight - lightestWeight}, " +
                $"largest class weight {largestClassWeight}.");
        }
    }

    private static void AssertSelectedShard(TestShardPlan plan, int expectedIndex)
    {
        Assert.AreEqual(expectedIndex, plan.SelectedShardIndex);
        Assert.AreEqual(plan.Shards[expectedIndex].Filter, plan.SelectedFilter);
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Planner_InvalidArgumentsAndUnreadableAssemblies_FailClosedAsync()
    {
        var testAssemblyPath = typeof(TestShardPlanContractTests).Assembly.Location;
        var missingAssemblyPath = Path.Combine(
            Path.GetDirectoryName(testAssemblyPath)!,
            $"missing-{Guid.NewGuid():N}.dll");
        var assemblyWithoutTestsPath = Path.Combine(
            Path.GetDirectoryName(testAssemblyPath)!,
            "RoslynMcp.Core.dll");
        var fixtureRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(TestShardPlanContractTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureRoot);
        var unreadableAssemblyPath = Path.Combine(fixtureRoot, "not-an-assembly.dll");
        await File.WriteAllTextAsync(unreadableAssemblyPath, "not managed metadata");

        try
        {
            var cases = new[]
            {
                new InvalidCase(testAssemblyPath, 0, 0, "TestShardCount must be between 1 and 16"),
                new InvalidCase(testAssemblyPath, 17, 0, "TestShardCount must be between 1 and 16"),
                new InvalidCase(testAssemblyPath, 2, 2, "TestShardIndex must be between 0 and 1"),
                new InvalidCase(missingAssemblyPath, 1, 0, "Test assembly not found"),
                new InvalidCase(unreadableAssemblyPath, 1, 0, null),
                new InvalidCase(
                    assemblyWithoutTestsPath,
                    1,
                    0,
                    "No runnable concrete MSTest classes were discovered"),
            };

            foreach (var testCase in cases)
            {
                var result = await RunPlannerAsync(
                    testCase.AssemblyPath,
                    testCase.ShardCount,
                    testCase.ShardIndex);
                Assert.AreNotEqual(
                    0,
                    result.ExitCode,
                    $"Planner unexpectedly succeeded. stdout={result.StdOut} stderr={result.StdErr}");
                if (testCase.ExpectedError is not null)
                {
                    StringAssert.Contains(
                        result.StdOut + result.StdErr,
                        testCase.ExpectedError,
                        $"Unexpected planner diagnostic. stdout={result.StdOut} stderr={result.StdErr}");
                }
            }
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [TestMethod]
    [TestCategory("Process")]
    public async Task Planner_AdapterCatalogParity_CoversInheritedCustomAndParameterizedTestsAsync()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var fixtureProjectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "RoslynMcp.ShardDiscoveryFixtures",
            "RoslynMcp.ShardDiscoveryFixtures.csproj");
        var testAssemblyDirectory = new DirectoryInfo(
            Path.GetDirectoryName(typeof(TestShardPlanContractTests).Assembly.Location)!);
        var targetFramework = testAssemblyDirectory.Name;
        var configuration = testAssemblyDirectory.Parent!.Name;
        var fixtureAssemblyPath = Path.Combine(
            Path.GetDirectoryName(fixtureProjectPath)!,
            "bin",
            configuration,
            targetFramework,
            "RoslynMcp.ShardDiscoveryFixtures.dll");

        var resultsRoot = Path.Combine(
            TestTempRoot.Current,
            nameof(TestShardPlanContractTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(resultsRoot);
        var resultsPath = Path.Combine(resultsRoot, "adapter.trx");

        try
        {
            var buildResult = await BuildFixtureAsync(fixtureProjectPath, configuration);
            Assert.AreEqual(
                0,
                buildResult.ExitCode,
                $"Fixture build failed. stdout={buildResult.StdOut} stderr={buildResult.StdErr}");
            Assert.IsTrue(File.Exists(fixtureAssemblyPath), $"Fixture assembly not found: {fixtureAssemblyPath}");

            var adapterResult = await RunAdapterAsync(
                fixtureAssemblyPath,
                resultsRoot);
            Assert.AreEqual(
                0,
                adapterResult.ExitCode,
                $"Adapter execution failed. stdout={adapterResult.StdOut} stderr={adapterResult.StdErr}");
            Assert.IsTrue(File.Exists(resultsPath), $"Adapter TRX not found: {resultsPath}");

            var parityResult = await RunPlannerAsync(
                fixtureAssemblyPath,
                shardCount: 1,
                shardIndex: 0,
                adapterResultsPath: resultsPath);
            AssertSucceeded(parityResult);

            var parityPlan = ParsePlan(parityResult.StdOut);
            Assert.IsTrue(parityPlan.AdapterParityVerified);
            Assert.AreEqual(3, parityPlan.AdapterClassCount);
            Assert.AreEqual(
                2,
                parityPlan.TestClasses.Single(testClass =>
                    testClass.ClassName ==
                    "RoslynMcp.ShardDiscoveryFixtures.ParameterizedTestClass").StaticCaseWeight);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "RoslynMcp.ShardDiscoveryFixtures.CustomAttributedTestClass",
                    "RoslynMcp.ShardDiscoveryFixtures.InheritedTestClass",
                    "RoslynMcp.ShardDiscoveryFixtures.ParameterizedTestClass",
                },
                parityPlan.TestClasses.Select(testClass => testClass.ClassName).ToArray());

            var adapterDocument = XDocument.Load(resultsPath);
            var unitTests = adapterDocument
                .Descendants()
                .Where(element => element.Name.LocalName == "UnitTest")
                .ToArray();
            var customClassDefinition = unitTests.Single(element =>
                element.Descendants().Any(child =>
                    child.Name.LocalName == "TestMethod" &&
                    (string?)child.Attribute("className") ==
                    "RoslynMcp.ShardDiscoveryFixtures.CustomAttributedTestClass"));
            customClassDefinition.Remove();
            var incompleteResultsPath = Path.Combine(resultsRoot, "adapter-incomplete.trx");
            adapterDocument.Save(incompleteResultsPath);

            var incompleteResult = await RunPlannerAsync(
                fixtureAssemblyPath,
                shardCount: 1,
                shardIndex: 0,
                adapterResultsPath: incompleteResultsPath);
            Assert.AreNotEqual(0, incompleteResult.ExitCode);
            StringAssert.Contains(
                incompleteResult.AllOutput,
                "Planner classes absent from adapter results");

            var expandedAdapterDocument = XDocument.Load(resultsPath);
            var fakeAdapterDefinition = new XElement(
                expandedAdapterDocument
                    .Descendants()
                    .First(element => element.Name.LocalName == "UnitTest"));
            var fakeTestMethod = fakeAdapterDefinition
                .Descendants()
                .Single(element => element.Name.LocalName == "TestMethod");
            fakeTestMethod.SetAttributeValue(
                "className",
                "RoslynMcp.ShardDiscoveryFixtures.AdapterOnlyTestClass");
            expandedAdapterDocument
                .Descendants()
                .Single(element => element.Name.LocalName == "TestDefinitions")
                .Add(fakeAdapterDefinition);
            var expandedResultsPath = Path.Combine(resultsRoot, "adapter-expanded.trx");
            expandedAdapterDocument.Save(expandedResultsPath);

            var expandedResult = await RunPlannerAsync(
                fixtureAssemblyPath,
                shardCount: 1,
                shardIndex: 0,
                adapterResultsPath: expandedResultsPath);
            Assert.AreNotEqual(0, expandedResult.ExitCode);
            StringAssert.Contains(
                expandedResult.AllOutput,
                "Adapter classes omitted by planner");
        }
        finally
        {
            TestFixtureFileSystem.DeleteDirectoryIfExists(resultsRoot);
        }
    }

    [TestMethod]
    public void CiRunSettings_TreatsAnEmptyShardAsAnError()
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var settingsPath = Path.Combine(repositoryRoot, "eng", "ci.runsettings");
        var document = XDocument.Load(settingsPath);

        Assert.AreEqual(
            "true",
            document.Root?
                .Element("RunConfiguration")?
                .Element("TreatNoTestsAsError")?
                .Value);
    }

    private static void AssertSafeExactFilter(ShardPlan shard)
    {
        var ordinalClasses = shard.Classes
            .OrderBy(className => className, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            ordinalClasses,
            shard.Classes,
            $"Shard {shard.Index} classes must be ordinally sorted.");

        foreach (var className in shard.Classes)
        {
            Assert.IsTrue(
                Regex.IsMatch(className, @"\A[A-Za-z_][A-Za-z0-9_.]*\z", RegexOptions.CultureInvariant),
                $"Class '{className}' contains an unsafe test-filter character.");
        }

        var expectedFilter = string.Join(
            "|",
            shard.Classes.Select(className => $"ClassName={className}"));
        Assert.AreEqual(expectedFilter, shard.Filter);
    }

    private static TestShardPlan ParsePlan(string json)
        => JsonSerializer.Deserialize<TestShardPlan>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Planner returned JSON null.");

    private static void AssertSucceeded(PwshScriptResult result)
        => Assert.AreEqual(
            0,
            result.ExitCode,
            $"Planner failed. stdout={result.StdOut} stderr={result.StdErr}");

    private static Task<PwshScriptResult> RunPlannerAsync(
        string testAssemblyPath,
        int shardCount,
        int shardIndex,
        string? adapterResultsPath = null,
        string? durationProfilePath = null,
        string? hostedImage = null)
    {
        var repositoryRoot = TestFixtureFileSystem.FindRepositoryRoot();
        var plannerPath = Path.Combine(repositoryRoot, "eng", "get-test-shard-plan.ps1");
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            plannerPath,
            "-TestAssemblyPath",
            testAssemblyPath,
            "-TestShardCount",
            shardCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-TestShardIndex",
            shardIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (adapterResultsPath is not null)
        {
            arguments.Add("-AdapterResultsPath");
            arguments.Add(adapterResultsPath);
        }
        if (durationProfilePath is not null)
        {
            arguments.Add("-DurationProfilePath");
            arguments.Add(durationProfilePath);
            arguments.Add("-HostedImage");
            arguments.Add(hostedImage!);
        }

        return PwshScriptRunner.RunAsync(
            arguments,
            workingDirectory: repositoryRoot,
            timeout: _processTimeout,
            description: "test-shard planner");
    }

    private static Task<PwshScriptResult> RunAdapterAsync(
        string fixtureAssemblyPath,
        string resultsRoot)
    {
        return PwshScriptRunner.RunExecutableAsync(
            "dotnet",
            [
                "vstest",
                fixtureAssemblyPath,
                "--Logger:trx;LogFileName=adapter.trx",
                $"--ResultsDirectory:{resultsRoot}",
            ],
            environment: null,
            workingDirectory: TestFixtureFileSystem.FindRepositoryRoot(),
            timeout: _processTimeout,
            description: "installed VSTest adapter");
    }

    private static Task<PwshScriptResult> BuildFixtureAsync(
        string fixtureProjectPath,
        string configuration)
    {
        return PwshScriptRunner.RunExecutableAsync(
            "dotnet",
            [
                "build",
                fixtureProjectPath,
                "--configuration",
                configuration,
                "--no-restore",
                "--nologo",
            ],
            environment: null,
            workingDirectory: TestFixtureFileSystem.FindRepositoryRoot(),
            timeout: _processTimeout,
            description: "shard-discovery fixture build");
    }

    private sealed record InvalidCase(
        string AssemblyPath,
        int ShardCount,
        int ShardIndex,
        string? ExpectedError);

    private sealed record TestShardPlan(
        int SchemaVersion,
        string TestAssemblyPath,
        int TestShardCount,
        int SelectedShardIndex,
        int TotalClassCount,
        int TotalStaticCaseWeight,
        bool AdapterParityVerified,
        int AdapterClassCount,
        TestClassPlan[] TestClasses,
        ShardPlan[] Shards,
        string SelectedFilter);

    private sealed record TestClassPlan(string ClassName, int StaticCaseWeight);

    private sealed record ShardPlan(
        int Index,
        int ClassCount,
        int StaticCaseWeight,
        string[] Classes,
        string Filter);
}
