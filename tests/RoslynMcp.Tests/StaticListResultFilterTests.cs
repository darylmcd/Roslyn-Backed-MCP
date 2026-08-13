using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using RoslynMcp.Host.Stdio.Middleware;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class StaticListResultFilterTests
{
    [TestMethod]
    public void NormalizeTools_SortsOrdinallyAndAddsPrivateCachingHints()
    {
        var result = new ListToolsResult
        {
            Tools =
            [
                new Tool { Name = "zeta", InputSchema = EmptyObjectSchema() },
                new Tool { Name = "Alpha", InputSchema = EmptyObjectSchema() },
                new Tool { Name = "beta", InputSchema = EmptyObjectSchema() },
            ],
        };

        StaticListResultFilter.Normalize(result);

        CollectionAssert.AreEqual(new[] { "Alpha", "beta", "zeta" }, result.Tools.Select(static tool => tool.Name).ToArray());
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, result.TimeToLive);
        Assert.AreEqual(CacheScope.Private, result.CacheScope);
    }

    [TestMethod]
    public void NormalizeOtherStaticLists_SortsAndAddsCachingHints()
    {
        var prompts = StaticListResultFilter.Normalize(new ListPromptsResult
        {
            Prompts = [new Prompt { Name = "z" }, new Prompt { Name = "a" }],
        });
        var resources = StaticListResultFilter.Normalize(new ListResourcesResult
        {
            Resources =
            [
                new Resource { Name = "z", Uri = "roslyn://z" },
                new Resource { Name = "a", Uri = "roslyn://a" },
            ],
        });
        var templates = StaticListResultFilter.Normalize(new ListResourceTemplatesResult
        {
            ResourceTemplates =
            [
                new ResourceTemplate { Name = "z", UriTemplate = "roslyn://z/{id}" },
                new ResourceTemplate { Name = "a", UriTemplate = "roslyn://a/{id}" },
            ],
        });

        CollectionAssert.AreEqual(new[] { "a", "z" }, prompts.Prompts.Select(static prompt => prompt.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "roslyn://a", "roslyn://z" },
            resources.Resources.Select(static resource => resource.Uri).ToArray());
        CollectionAssert.AreEqual(
            new[] { "roslyn://a/{id}", "roslyn://z/{id}" },
            templates.ResourceTemplates.Select(static template => template.UriTemplate).ToArray());
        Assert.AreEqual(CacheScope.Private, prompts.CacheScope);
        Assert.AreEqual(CacheScope.Private, resources.CacheScope);
        Assert.AreEqual(CacheScope.Private, templates.CacheScope);
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, prompts.TimeToLive);
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, resources.TimeToLive);
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, templates.TimeToLive);
    }

    [TestMethod]
    public void NormalizeResourceRead_MarksDynamicBodyImmediatelyStaleAndPrivate()
    {
        var content = new TextResourceContents { Uri = "roslyn://workspace/status/id", Text = "{}" };
        var result = new ReadResourceResult { Contents = [content] };

        var normalized = ResourceReadResultFilter.Normalize(result);

        Assert.AreSame(result, normalized);
        Assert.HasCount(1, normalized.Contents);
        Assert.AreSame(content, normalized.Contents[0]);
        Assert.AreEqual(TimeSpan.Zero, normalized.TimeToLive);
        Assert.AreEqual(CacheScope.Private, normalized.CacheScope);
    }

    private static System.Text.Json.JsonElement EmptyObjectSchema() =>
        System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();
}
