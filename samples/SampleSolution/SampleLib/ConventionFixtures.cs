// Test fixture for find_unused_symbols convention-awareness. Defines stub base types
// inline so the sample project does not need NuGet dependencies on Microsoft.AspNetCore,
// Microsoft.EntityFrameworkCore, FluentValidation, Microsoft.AspNetCore.SignalR,
// Microsoft.Extensions.Diagnostics.HealthChecks, ModelContextProtocol.Server, xunit.
// Detection in UnusedCodeAnalyzer is name-shape based.
namespace SampleLib.ConventionFixtures;

// Stub base types — name shape is enough for detection.
public abstract class AbstractValidator<T> { }
public abstract class Hub { }
public abstract class PageModel { }
public abstract class Migration { }
public abstract class ModelSnapshot { }
public sealed class HttpContext { }
public sealed class HealthReport { }

// Stub attributes — simple-name match is enough for detection.
[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerToolTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerPromptTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class McpServerResourceTypeAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class)]
public sealed class CollectionDefinitionAttribute : Attribute
{
    public CollectionDefinitionAttribute(string name) { Name = name; }
    public string Name { get; }
}

// Intentionally-similar attribute used to assert that name-shape matching does NOT
// accidentally swallow unrelated attributes whose names merely contain the substrings.
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotMcpServerToolTypeAttribute : Attribute { }

// Convention-shaped samples — these MUST NOT be reported as unused when
// excludeConventionInvoked is true (the default).
public sealed class SampleValidator : AbstractValidator<string>
{
    public void ConfigureRules() { }
}

public sealed class ChatHub : Hub
{
    public void SendMessage(string message) { }
}

public sealed class IndexModel : PageModel
{
    public void OnGet() { }
}

public sealed class MyDbContextModelSnapshot : ModelSnapshot
{
}

public sealed class LoggingMiddleware
{
    public Task InvokeAsync(HttpContext context) => Task.CompletedTask;
}

// ASP.NET HealthCheck response-writer shape: bound to
// HealthCheckOptions.ResponseWriter via delegate reference, so the declaring type
// has no direct C# call site.
public sealed class HealthResponseWriter
{
    public Task WriteAsync(HttpContext context, HealthReport report) => Task.CompletedTask;
}

// MCP server catalog types — discovered by reflection via the
// ModelContextProtocol.Server host.
[McpServerToolType]
public sealed class SampleMcpToolCatalog
{
    public void Register() { }
}

[McpServerPromptType]
public sealed class SampleMcpPromptCatalog
{
    public void Register() { }
}

[McpServerResourceType]
public sealed class SampleMcpResourceCatalog
{
    public void Register() { }
}

// xUnit collection-definition holder — xUnit resolves this via attribute reflection.
[CollectionDefinition("WebHost serial collection")]
public sealed class WebHostSerialCollection
{
}

// Control: this IS truly unused and SHOULD still be reported even with the convention
// filter on. It does not match any convention shape.
internal sealed class TrulyUnusedConcreteType
{
    public void Unused() { }
}

// Negative control: the attribute is named similarly to the MCP family but is NOT
// on the whitelist. The holder must surface as unused when convention filter is on.
[NotMcpServerToolType]
internal sealed class NotConventionInvokedHolder
{
}
