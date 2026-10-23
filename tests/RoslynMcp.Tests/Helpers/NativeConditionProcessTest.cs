using System.Text.Json;
using System.Xml.Linq;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests.Helpers;

/// <summary>Runs one exact native condition test in a fresh process with explicitly owned environment changes.</summary>
internal static class NativeConditionProcessTest
{
    private const string ChildMarker = "ROSLYNMCP_NATIVE_CONDITION_TEST_CHILD";

    internal static Task RunAsync(Type testClass, string methodName,
        IReadOnlyDictionary<string, string?> environment, Action childAction)
        => RunAsync(testClass, methodName, environment, () => { childAction(); return Task.CompletedTask; });

    internal static async Task RunAsync(Type testClass, string methodName,
        IReadOnlyDictionary<string, string?> environment, Func<Task> childAction)
    {
        var assembly = testClass.Assembly.Location;
        var qualifiedMethod = testClass.FullName + "." + methodName;
        var identity = JsonSerializer.Serialize(new[] { assembly, qualifiedMethod });
        if (Environment.GetEnvironmentVariable(ChildMarker) == identity)
        {
            foreach (var pair in environment)
                Assert.AreEqual(pair.Value, Environment.GetEnvironmentVariable(pair.Key),
                    "The isolated native process must receive the requested environment.");
            await childAction().ConfigureAwait(false);
            return;
        }

        Assert.IsFalse(environment.Keys.Any(key => key.Equals(ChildMarker, StringComparison.OrdinalIgnoreCase)),
            "Callers cannot replace the exact child-test identity.");
        var childEnvironment = environment.ToDictionary(pair => pair.Key, pair => pair.Value);
        childEnvironment.Add(ChildMarker, identity);
        var results = Path.Combine(TestTempRoot.Current, nameof(NativeConditionProcessTest), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(results);
            var run = await PwshScriptRunner.RunExecutableAsync("dotnet",
                ["vstest", assembly, "/TestCaseFilter:FullyQualifiedName=" + qualifiedMethod,
                    "/Logger:trx;LogFileName=native-condition.trx", "/ResultsDirectory:" + results],
                childEnvironment,
                workingDirectory: Path.GetDirectoryName(assembly),
                timeout: TimeSpan.FromSeconds(60),
                description: "isolated native condition control");
            Assert.AreEqual(0, run.ExitCode, run.StdOut + Environment.NewLine + run.StdErr);
            var trx = XDocument.Load(Path.Combine(results, "native-condition.trx"));
            var counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
            Assert.AreEqual("1", (string?)counters.Attribute("executed"));
            Assert.AreEqual("1", (string?)counters.Attribute("passed"));
            Assert.AreEqual("0", (string?)counters.Attribute("failed"));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Native condition child results cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(results)));
        }
    }
}
