using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// <c>dotnet test</c> resolves its execution mode — classic VSTest, or the .NET 10 SDK's
/// native Microsoft.Testing.Platform (MTP) mode — from the nearest <c>global.json</c>'s
/// <c>test.runner</c> setting, walking up from the target project's directory exactly like the
/// dotnet CLI's own SDK-resolution walk. <see cref="RoslynMcp.Roslyn.Services.TestRunnerService"/>
/// needs this to decide whether it can run an MTP-only test framework (TUnit) at all: verified
/// against a real TUnit project, the legacy VSTest-mode MTP bridge
/// (<c>-p:TestingPlatformDotnetTestSupport=true --</c>) is hard-removed on the .NET 10 SDK
/// ("Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10
/// SDK and later"), so native mode's <c>global.json</c> opt-in is the only path — there is no
/// fallback argument shape to build when it's absent.
/// https://learn.microsoft.com/dotnet/core/testing/unit-testing-with-dotnet-test#mtp-mode-of-dotnet-test
/// </summary>
internal static class DotnetTestModeResolver
{
    public static bool UsesNativeMtpDotnetTest(string startDirectory, ILogger? logger = null)
    {
        for (var directory = startDirectory;
             !string.IsNullOrEmpty(directory);
             directory = Path.GetDirectoryName(directory))
        {
            var globalJsonPath = Path.Combine(directory, "global.json");
            if (File.Exists(globalJsonPath))
            {
                return ReadsNativeMtpRunner(globalJsonPath, logger);
            }
        }

        return false;
    }

    // global.json officially allows JavaScript/C#-style comments and trailing commas
    // (https://learn.microsoft.com/dotnet/core/tools/global-json#comments-in-globaljson),
    // which System.Text.Json rejects by default — without this, a validly-commented global.json
    // that DOES opt into MTP would throw JsonException below and silently fall back to "assume
    // VSTest mode", the exact silent-omission this whole check exists to prevent.
    private static readonly JsonDocumentOptions GlobalJsonParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static bool ReadsNativeMtpRunner(string globalJsonPath, ILogger? logger)
    {
        try
        {
            using var stream = File.OpenRead(globalJsonPath);
            using var document = JsonDocument.Parse(stream, GlobalJsonParseOptions);
            return document.RootElement.TryGetProperty("test", out var testElement) &&
                   testElement.TryGetProperty("runner", out var runnerElement) &&
                   runnerElement.ValueKind == JsonValueKind.String &&
                   string.Equals(
                       runnerElement.GetString(),
                       "Microsoft.Testing.Platform",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A malformed or unreadable global.json is a pre-existing repo problem outside
            // test_run's scope; fall back to the default (VSTest mode) rather than failing
            // the test run over it.
            logger?.LogDebug(ex, "Failed to read global.json at {Path}; assuming VSTest mode.", globalJsonPath);
            return false;
        }
    }
}
