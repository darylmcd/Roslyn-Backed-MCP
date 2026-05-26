using Microsoft.CodeAnalysis;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public class ProjectGraphHelpersTests
{
    [TestMethod]
    public void Returns_True_When_Direct_Reference_Closes_Cycle()
    {
        using var workspace = new AdhocWorkspace();
        var a = workspace.AddProject("A", LanguageNames.CSharp);
        var b = workspace.AddProject("B", LanguageNames.CSharp);
        var solution = workspace.CurrentSolution
            .AddProjectReference(b.Id, new ProjectReference(a.Id));

        var source = solution.GetProject(a.Id)!;
        var target = solution.GetProject(b.Id)!;

        Assert.IsTrue(ProjectGraphHelpers.WouldCreateProjectReferenceCycle(source, target));
    }

    [TestMethod]
    public void Returns_True_When_Self_Reference()
    {
        using var workspace = new AdhocWorkspace();
        var a = workspace.AddProject("A", LanguageNames.CSharp);
        var project = workspace.CurrentSolution.GetProject(a.Id)!;

        Assert.IsTrue(ProjectGraphHelpers.WouldCreateProjectReferenceCycle(project, project));
    }

    [TestMethod]
    public void Returns_True_When_Transitive_Reference_Closes_Cycle()
    {
        using var workspace = new AdhocWorkspace();
        var a = workspace.AddProject("A", LanguageNames.CSharp);
        var b = workspace.AddProject("B", LanguageNames.CSharp);
        var c = workspace.AddProject("C", LanguageNames.CSharp);
        var solution = workspace.CurrentSolution
            .AddProjectReference(b.Id, new ProjectReference(a.Id))
            .AddProjectReference(c.Id, new ProjectReference(b.Id));

        var source = solution.GetProject(a.Id)!;
        var target = solution.GetProject(c.Id)!;

        Assert.IsTrue(ProjectGraphHelpers.WouldCreateProjectReferenceCycle(source, target));
    }

    [TestMethod]
    public void Returns_False_For_Acyclic_Graph()
    {
        using var workspace = new AdhocWorkspace();
        var a = workspace.AddProject("A", LanguageNames.CSharp);
        var b = workspace.AddProject("B", LanguageNames.CSharp);
        var c = workspace.AddProject("C", LanguageNames.CSharp);
        var solution = workspace.CurrentSolution
            .AddProjectReference(b.Id, new ProjectReference(c.Id));

        var source = solution.GetProject(a.Id)!;
        var target = solution.GetProject(b.Id)!;

        Assert.IsFalse(ProjectGraphHelpers.WouldCreateProjectReferenceCycle(source, target));
    }
}
