using RoslynMcp.Host.Stdio.Configuration;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class ScriptingOptionsEnvironmentBinderTests
{
    [TestMethod]
    public void Bind_UnsetAndPlaceholderValues_UseDefaults()
    {
        var values = new Dictionary<string, string?>
        {
            [ScriptingOptionsEnvironmentBinder.TimeoutVariable] =
                "${user_config.SCRIPT_TIMEOUT_SECONDS}",
        };

        var actual = Bind(values);

        Assert.AreEqual(new ScriptingServiceOptions(), actual);
    }

    [TestMethod]
    public void Bind_ValidValues_ProjectsEveryOption()
    {
        var values = new Dictionary<string, string?>
        {
            [ScriptingOptionsEnvironmentBinder.TimeoutVariable] = "12",
            [ScriptingOptionsEnvironmentBinder.HeartbeatVariable] = "250",
            [ScriptingOptionsEnvironmentBinder.StuckWarningVariable] = "3",
            [ScriptingOptionsEnvironmentBinder.WatchdogGraceVariable] = "4",
            [ScriptingOptionsEnvironmentBinder.MaxConcurrentVariable] = "2",
            [ScriptingOptionsEnvironmentBinder.SlotWaitVariable] = "6",
            [ScriptingOptionsEnvironmentBinder.MaxAbandonedVariable] = "5",
        };

        var actual = Bind(values);

        Assert.AreEqual(12, actual.TimeoutSeconds);
        Assert.AreEqual(250, actual.HeartbeatIntervalMs);
        Assert.AreEqual(3, actual.StuckWarningSeconds);
        Assert.AreEqual(4, actual.WatchdogGraceSeconds);
        Assert.AreEqual(2, actual.MaxConcurrentEvaluations);
        Assert.AreEqual(6, actual.ConcurrencySlotAcquireTimeoutSeconds);
        Assert.AreEqual(5, actual.MaxAbandonedEvaluations);
    }

    [TestMethod]
    [DataRow(ScriptingOptionsEnvironmentBinder.TimeoutVariable, "not-a-number")]
    [DataRow(ScriptingOptionsEnvironmentBinder.TimeoutVariable, "0")]
    [DataRow(ScriptingOptionsEnvironmentBinder.HeartbeatVariable, "-1")]
    [DataRow(ScriptingOptionsEnvironmentBinder.WatchdogGraceVariable, "-1")]
    [DataRow(ScriptingOptionsEnvironmentBinder.MaxConcurrentVariable, "0")]
    public void Bind_MalformedOrOutOfRangeValue_FailsWithVariableNameOnly(
        string variableName,
        string value)
    {
        var values = new Dictionary<string, string?> { [variableName] = value };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => Bind(values));

        StringAssert.Contains(failure.Message, variableName);
        if (string.Equals(value, "not-a-number", StringComparison.Ordinal))
        {
            Assert.IsFalse(failure.Message.Contains(value, StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void Bind_TimeoutPlusGraceBeyondRuntimeLimit_FailsAtStartupBoundary()
    {
        var values = new Dictionary<string, string?>
        {
            [ScriptingOptionsEnvironmentBinder.TimeoutVariable] =
                ScriptingServiceOptions.MaxTimerDurationSeconds.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            [ScriptingOptionsEnvironmentBinder.WatchdogGraceVariable] = "1",
        };

        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => Bind(values));

        StringAssert.Contains(failure.Message, ScriptingOptionsEnvironmentBinder.TimeoutVariable);
        StringAssert.Contains(failure.Message, ScriptingOptionsEnvironmentBinder.WatchdogGraceVariable);
    }

    [TestMethod]
    public void Bind_MaximumRuntimeBudgetBoundary_IsAccepted()
    {
        var values = new Dictionary<string, string?>
        {
            [ScriptingOptionsEnvironmentBinder.TimeoutVariable] =
                (ScriptingServiceOptions.MaxTimerDurationSeconds - 1).ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
            [ScriptingOptionsEnvironmentBinder.WatchdogGraceVariable] = "1",
        };

        var actual = Bind(values);

        Assert.AreEqual(
            ScriptingServiceOptions.MaxTimerDurationSeconds,
            checked(actual.TimeoutSeconds + actual.WatchdogGraceSeconds));
    }

    private static ScriptingServiceOptions Bind(IReadOnlyDictionary<string, string?> values) =>
        ScriptingOptionsEnvironmentBinder.Bind(
            variableName => values.TryGetValue(variableName, out var value) ? value : null);
}
