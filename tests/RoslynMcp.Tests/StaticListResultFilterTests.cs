using System.Text.Json.Nodes;
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
        var meta = new JsonObject { ["sentinel"] = "tools" };
        var result = new ListToolsResult
        {
            Tools =
            [
                new Tool { Name = "zeta", InputSchema = EmptyObjectSchema() },
                new Tool { Name = "Alpha", InputSchema = EmptyObjectSchema() },
                new Tool { Name = "beta", InputSchema = EmptyObjectSchema() },
            ],
            Meta = meta,
        };

        StaticListResultFilter.Normalize(result, supportsCachingHints: true);

        CollectionAssert.AreEqual(new[] { "Alpha", "beta", "zeta" }, result.Tools.Select(static tool => tool.Name).ToArray());
        Assert.AreEqual(StaticListResultFilter.CacheTimeToLive, result.TimeToLive);
        Assert.AreEqual(CacheScope.Private, result.CacheScope);
        Assert.AreSame(meta, result.Meta);
        Assert.AreEqual("tools", meta["sentinel"]?.GetValue<string>());
    }

    [TestMethod]
    public void NormalizeOtherStaticLists_SortsAndAddsCachingHints()
    {
        var promptMeta = new JsonObject { ["sentinel"] = "prompts" };
        var resourceMeta = new JsonObject { ["sentinel"] = "resources" };
        var templateMeta = new JsonObject { ["sentinel"] = "templates" };
        var prompts = StaticListResultFilter.Normalize(new ListPromptsResult
        {
            Prompts = [new Prompt { Name = "z" }, new Prompt { Name = "a" }],
            Meta = promptMeta,
        }, supportsCachingHints: true);
        var resources = StaticListResultFilter.Normalize(new ListResourcesResult
        {
            Resources =
            [
                new Resource { Name = "z", Uri = "roslyn://z" },
                new Resource { Name = "a", Uri = "roslyn://a" },
            ],
            Meta = resourceMeta,
        }, supportsCachingHints: true);
        var templates = StaticListResultFilter.Normalize(new ListResourceTemplatesResult
        {
            ResourceTemplates =
            [
                new ResourceTemplate { Name = "z", UriTemplate = "roslyn://z/{id}" },
                new ResourceTemplate { Name = "a", UriTemplate = "roslyn://a/{id}" },
            ],
            Meta = templateMeta,
        }, supportsCachingHints: true);

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
        Assert.AreSame(promptMeta, prompts.Meta);
        Assert.AreSame(resourceMeta, resources.Meta);
        Assert.AreSame(templateMeta, templates.Meta);
        Assert.AreEqual("prompts", promptMeta["sentinel"]?.GetValue<string>());
        Assert.AreEqual("resources", resourceMeta["sentinel"]?.GetValue<string>());
        Assert.AreEqual("templates", templateMeta["sentinel"]?.GetValue<string>());
    }

    [TestMethod]
    public void NormalizeResourceRead_MarksDynamicBodyImmediatelyStaleAndPrivate()
    {
        var content = new TextResourceContents { Uri = "roslyn://workspace/status/id", Text = "{}" };
        var meta = new JsonObject { ["sentinel"] = "resource-read" };
        var result = new ReadResourceResult { Contents = [content], Meta = meta };

        var normalized = ResourceReadResultFilter.Normalize(result, supportsCachingHints: true);

        Assert.AreSame(result, normalized);
        Assert.HasCount(1, normalized.Contents);
        Assert.AreSame(content, normalized.Contents[0]);
        Assert.AreEqual(TimeSpan.Zero, normalized.TimeToLive);
        Assert.AreEqual(CacheScope.Private, normalized.CacheScope);
        Assert.AreSame(meta, normalized.Meta);
        Assert.AreEqual("resource-read", meta["sentinel"]?.GetValue<string>());
    }

    [TestMethod]
    public void NormalizeLegacyResults_RemovesDraftOnlyCachingHintsWithoutChangingPayloads()
    {
        var tool = new Tool { Name = "tool", InputSchema = EmptyObjectSchema() };
        var toolMeta = new JsonObject { ["sentinel"] = "tools" };
        var tools = StaticListResultFilter.Normalize(new ListToolsResult
        {
            Tools = [tool],
            TimeToLive = TimeSpan.FromSeconds(1),
            CacheScope = CacheScope.Public,
            Meta = toolMeta,
        }, supportsCachingHints: false);

        var content = new TextResourceContents { Uri = "roslyn://server/catalog", Text = "{}" };
        var resourceMeta = new JsonObject { ["sentinel"] = "resource-read" };
        var resourceRead = ResourceReadResultFilter.Normalize(new ReadResourceResult
        {
            Contents = [content],
            TimeToLive = TimeSpan.FromSeconds(1),
            CacheScope = CacheScope.Public,
            Meta = resourceMeta,
        }, supportsCachingHints: false);

        Assert.HasCount(1, tools.Tools);
        Assert.AreSame(tool, tools.Tools[0]);
        Assert.IsNull(tools.TimeToLive);
        Assert.IsNull(tools.CacheScope);
        Assert.AreSame(toolMeta, tools.Meta);
        Assert.AreEqual("tools", toolMeta["sentinel"]?.GetValue<string>());

        Assert.HasCount(1, resourceRead.Contents);
        Assert.AreSame(content, resourceRead.Contents[0]);
        Assert.IsNull(resourceRead.TimeToLive);
        Assert.IsNull(resourceRead.CacheScope);
        Assert.AreSame(resourceMeta, resourceRead.Meta);
        Assert.AreEqual("resource-read", resourceMeta["sentinel"]?.GetValue<string>());
    }

    private static System.Text.Json.JsonElement EmptyObjectSchema()
    {
        using var document = System.Text.Json.JsonDocument.Parse("{\"type\":\"object\"}");
        return document.RootElement.Clone();
    }
}
