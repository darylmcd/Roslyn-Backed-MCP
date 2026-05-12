using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

/// <summary>
/// Coverage for code-fix-providers-missing-ca: <see cref="CodeFixProviderRegistry"/> must
/// surface providers for both compiler diagnostics (CS*) and analyzer diagnostics (CA*/IDE*)
/// rather than the previous CS8019-only hardcoded path.
/// </summary>
[TestClass]
public sealed class CodeFixProviderRegistryTests
{
    [TestMethod]
    public void Registry_LoadsAtLeastOneStaticProvider()
    {
        // Force the IDE Features assembly to be loaded before the registry probes for it. In
        // unit-test isolation the assembly isn't loaded into the AppDomain until something
        // (the test or production code) touches it; the registry uses Assembly.Load by name
        // which only succeeds if the assembly is already loaded.
        _ = typeof(Microsoft.CodeAnalysis.CSharp.Formatting.CSharpFormattingOptions);

        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        // Sanity: probe a handful of well-known diagnostic ids — at least one should resolve.
        // Different Roslyn versions ship different IDE/CS diagnostic ids; we just verify the
        // loader actually pulled providers, not which exact ids.
        string[] knownIds = ["CS8019", "IDE0005", "IDE0044", "CS0168", "CS0219", "CS1591"];
        var anyResolved = knownIds.Any(id => registry.GetProvidersFor(id).Count > 0);

        Assert.IsTrue(anyResolved,
            "Registry must expose at least one provider across well-known diagnostic ids " +
            $"({string.Join(", ", knownIds)}). The static loader likely failed to load " +
            "Microsoft.CodeAnalysis.CSharp.Features.");
    }

    [TestMethod]
    public void Registry_UnknownDiagnostic_ReturnsEmpty()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        var providers = registry.GetProvidersFor("ZZ9999");
        Assert.AreEqual(0, providers.Count);
    }

    [TestMethod]
    public void FirstProviderFor_ReturnsNullForUnknownDiagnostic()
    {
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);
        Assert.IsNull(registry.FirstProviderFor("ZZ9999"));
    }

    /// <summary>
    /// Validates the documented limitation: CA-series rules from Microsoft.CodeAnalysis.NetAnalyzers
    /// (e.g. CA1826, CA1848) return empty <c>supportedFixes</c> from the static-reflection registry
    /// because their fix providers require Roslyn workspace services injected via constructor — they
    /// have no parameterless constructor and cannot be instantiated by <see cref="Activator.CreateInstance"/>.
    ///
    /// This test pins the documented behavior so callers know to use get_code_actions +
    /// preview_code_action for CA rules instead of relying on <c>supportedFixes</c>.
    /// See: diagnostic-details-empty-supportedfixes-ca-rules, gh #620.
    /// </summary>
    [TestMethod]
    public void Registry_CaSeriesRules_ReturnEmptySupportedFixes_DocumentedLimitation()
    {
        // The registry's static-reflection path cannot instantiate CA fix providers because they
        // require Roslyn workspace services. This test validates that the registry correctly
        // returns empty for CA-series ids — confirming the documented behavior rather than a bug
        // being silently ignored.
        var registry = new CodeFixProviderRegistry(NullLogger<CodeFixProviderRegistry>.Instance);

        // Representative CA rules that ship with fix providers in NetAnalyzers but whose
        // providers require constructor injection. The static-reflection path must return empty
        // for all of them — this is the documented limitation.
        string[] caRuleIds = ["CA1826", "CA1848", "CA1822", "CA2201", "CA1416"];
        foreach (var caId in caRuleIds)
        {
            var providers = registry.GetProvidersFor(caId);
            Assert.AreEqual(0, providers.Count,
                $"CA-series rule '{caId}' must return empty supportedFixes from the static-reflection " +
                "registry. CA fix providers require Roslyn workspace services (no parameterless ctor) " +
                "and are not enumerable via static reflection. Callers must use get_code_actions + " +
                "preview_code_action to apply CA fixes at a specific document location.");
        }
    }
}
