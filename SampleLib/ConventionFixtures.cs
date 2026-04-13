// Test fixture for find_unused_symbols convention-awareness. Defines stub base types
// inline so the sample project does not need NuGet dependencies on Microsoft.AspNetCore,
// Microsoft.EntityFrameworkCore, FluentValidation, Microsoft.AspNetCore.SignalR.
// Detection in UnusedCodeAnalyzer is name-shape based.
namespace SampleLib.ConventionFixtures;

// Stub base types — name shape is enough for detection.
public abstract class AbstractValidator<T> { }
public abstract class Hub { }
public abstract class PageModel { }
public abstract class Migration { }
public abstract class ModelSnapshot { }
public sealed class HttpContext { }

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

// Control: this IS truly unused and SHOULD still be reported even with the convention
// filter on. It does not match any convention shape.
internal sealed class TrulyUnusedConcreteType
{
    public void Unused() { }
}
