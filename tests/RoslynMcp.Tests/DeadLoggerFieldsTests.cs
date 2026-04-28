using System.Reflection;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for backlog row <c>dead-logger-fields-roslyn-services-batch-1</c>.
///
/// Four services in <see cref="RoslynMcp.Roslyn.Services"/> previously declared a
/// <c>private readonly ILogger&lt;T&gt; _logger</c> field that was assigned in the
/// constructor but never read by any method. This test asserts that the field has
/// been removed (and is not reintroduced) by reflecting on the type and confirming
/// no instance field named <c>_logger</c> exists.
///
/// If a future change legitimately needs a logger on one of these services, prefer
/// adding it back together with the call sites that actually emit log records — and
/// then update this test to drop the type from <c>TypesThatMustNotHaveDeadLoggerFields</c>.
/// </summary>
[TestClass]
public sealed class DeadLoggerFieldsTests
{
    private static readonly Type[] TypesThatMustNotHaveDeadLoggerFields =
    [
        typeof(BulkRefactoringService),
        typeof(CodeMetricsService),
        typeof(CompletionService),
        typeof(ConsumerAnalysisService),
    ];

    [TestMethod]
    public void GuardedServices_DoNotDeclareDeadLoggerField()
    {
        foreach (var type in TypesThatMustNotHaveDeadLoggerFields)
        {
            var field = type.GetField(
                "_logger",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNull(
                field,
                $"{type.FullName} declares a '_logger' field. The dead-logger-fields-roslyn-services-batch-1 sweep removed this field because no method ever read it. If a logger is now genuinely needed, drop {type.Name} from TypesThatMustNotHaveDeadLoggerFields and add the call sites in the same change.");
        }
    }
}
