using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

internal enum FeatureProviderLoadFailureKind
{
    AssemblyLoad,
    TypeLoad,
    MissingParameterlessConstructor,
    ConstructorFailure,
}

internal sealed record FeatureProviderLoadFailure(
    FeatureProviderLoadFailureKind Kind,
    string? ProviderTypeName);

internal sealed record FeatureProviderLoadResult<TProvider>(
    ImmutableArray<TProvider> Providers,
    ImmutableArray<FeatureProviderLoadFailure> Failures)
    where TProvider : class
{
    internal int FailedProviderCount => Failures.Count(failure =>
        failure.Kind is not FeatureProviderLoadFailureKind.MissingParameterlessConstructor);

    internal int SkippedProviderCount => Failures.Count(failure =>
        failure.Kind is FeatureProviderLoadFailureKind.MissingParameterlessConstructor);

    internal bool IsComplete => FailedProviderCount == 0;
}

/// <summary>
/// Discovers and constructs Roslyn feature providers through one typed, observable result model.
/// </summary>
internal static class CSharpFeatureProviderLoader
{
    private const string _featuresAssemblyName = "Microsoft.CodeAnalysis.CSharp.Features";

    internal static FeatureProviderLoadResult<TProvider> Load<TProvider>(
        ILogger logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        where TProvider : class =>
        LoadFromAssemblyFactory<TProvider>(
            () => Assembly.Load(_featuresAssemblyName),
            logger,
            exceptionReporter);

    internal static FeatureProviderLoadResult<TProvider> LoadFromAssemblyFactory<TProvider>(
        Func<Assembly> assemblyFactory,
        ILogger logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        where TProvider : class
    {
        ArgumentNullException.ThrowIfNull(assemblyFactory);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return LoadFromTypeSource<TProvider>(
                () => assemblyFactory().GetTypes(),
                logger,
                exceptionReporter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportFailure(exceptionReporter, ex);
            var result = new FeatureProviderLoadResult<TProvider>(
                [],
                [new FeatureProviderLoadFailure(FeatureProviderLoadFailureKind.AssemblyLoad, null)]);
            LogSummary(logger, result);
            return result;
        }
    }

    internal static FeatureProviderLoadResult<TProvider> LoadFromTypeSource<TProvider>(
        Func<Type[]> typeSource,
        ILogger logger,
        IUnexpectedExceptionReporter? exceptionReporter = null)
        where TProvider : class
    {
        ArgumentNullException.ThrowIfNull(typeSource);
        ArgumentNullException.ThrowIfNull(logger);

        var failures = ImmutableArray.CreateBuilder<FeatureProviderLoadFailure>();
        IEnumerable<Type> types;
        try
        {
            types = typeSource();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var loaderException in ex.LoaderExceptions.OfType<Exception>())
            {
                ReportFailure(exceptionReporter, loaderException);
                failures.Add(new FeatureProviderLoadFailure(
                    FeatureProviderLoadFailureKind.TypeLoad,
                    null));
            }

            types = ex.Types.OfType<Type>();
        }

        var providers = ImmutableArray.CreateBuilder<TProvider>();
        foreach (var type in types.Where(type =>
                     !type.IsAbstract && typeof(TProvider).IsAssignableFrom(type)))
        {
            var providerTypeName = type.FullName ?? type.Name;
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                failures.Add(new FeatureProviderLoadFailure(
                    FeatureProviderLoadFailureKind.MissingParameterlessConstructor,
                    providerTypeName));
                continue;
            }

            try
            {
                if (Activator.CreateInstance(type) is TProvider provider)
                {
                    providers.Add(provider);
                }
                else
                {
                    failures.Add(new FeatureProviderLoadFailure(
                        FeatureProviderLoadFailureKind.ConstructorFailure,
                        providerTypeName));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ReportFailure(exceptionReporter, ex);
                failures.Add(new FeatureProviderLoadFailure(
                    FeatureProviderLoadFailureKind.ConstructorFailure,
                    providerTypeName));
            }
        }

        var result = new FeatureProviderLoadResult<TProvider>(providers.ToImmutable(), failures.ToImmutable());
        LogSummary(logger, result);
        return result;
    }

    private static void ReportFailure(
        IUnexpectedExceptionReporter? exceptionReporter,
        Exception exception) =>
        UnexpectedExceptionReporting.Report(
            exceptionReporter,
            exception,
            UnexpectedExceptionCategory.AnalyzerLoad);

    private static void LogSummary<TProvider>(
        ILogger logger,
        FeatureProviderLoadResult<TProvider> result)
        where TProvider : class
    {
        logger.LogInformation(
            "Feature provider load for {ProviderKind}: loaded={LoadedCount}, assemblyLoadFailures={AssemblyLoadFailureCount}, typeLoadFailures={TypeLoadFailureCount}, skippedNoConstructor={SkippedCount}, constructorFailures={ConstructorFailureCount}",
            typeof(TProvider).Name,
            result.Providers.Length,
            result.Failures.Count(failure => failure.Kind is FeatureProviderLoadFailureKind.AssemblyLoad),
            result.Failures.Count(failure => failure.Kind is FeatureProviderLoadFailureKind.TypeLoad),
            result.SkippedProviderCount,
            result.Failures.Count(failure => failure.Kind is FeatureProviderLoadFailureKind.ConstructorFailure));
    }
}
