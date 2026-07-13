using System.Reflection;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Regression guard for backlog rows <c>dead-logger-fields-roslyn-services-batch-1</c>,
/// <c>dead-logger-fields-roslyn-services-batch-2</c>, and
/// <c>dead-logger-fields-roslyn-services-batch-3</c>.
///
/// Fourteen services in <see cref="RoslynMcp.Roslyn.Services"/> previously declared a
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
        typeof(DiagnosticService),
        typeof(FlowAnalysisService),
        typeof(MutationAnalysisService),
        typeof(NamespaceDependencyService),
        typeof(OperationService),
        typeof(RecordFieldAdditionService),
        typeof(TypeExtractionService),
        typeof(TypeMoveService),
        typeof(DuplicateMethodDetectorService),
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
                $"{type.FullName} declares a '_logger' field. The dead-logger-fields-roslyn-services sweeps removed this field because no method ever read it. If a logger is now genuinely needed, drop {type.Name} from TypesThatMustNotHaveDeadLoggerFields and add the call sites in the same change.");
        }
    }

    [TestMethod]
    public void DuplicateMethodDetector_DoesNotDeclareDeadCompilationCacheField()
    {
        var field = typeof(DuplicateMethodDetectorService).GetField(
            "_compilationCache",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNull(
            field,
            $"{typeof(DuplicateMethodDetectorService).FullName} declares a '_compilationCache' field. The duplicate-method detector is syntax-only and does not need a compilation cache dependency.");
    }

    [TestMethod]
    public void RestructureStructuralRewriter_DoesNotDeclareDeadPatternPlaceholderNamesField()
    {
        var nestedType = typeof(RestructureService).GetNestedType(
            "StructuralRewriter",
            BindingFlags.NonPublic);

        Assert.IsNotNull(nestedType, "RestructureService.StructuralRewriter should remain discoverable by reflection.");

        var field = nestedType.GetField(
            "_patternPlaceholderNames",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNull(
            field,
            "RestructureService.StructuralRewriter declares a '_patternPlaceholderNames' field. " +
            "Pattern placeholders are validated before the rewriter is constructed; the rewriter only reads goal placeholders.");
    }
}
