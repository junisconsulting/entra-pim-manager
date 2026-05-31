namespace EntraPimManager.Tests.Services;

using EntraPimManager.Core.Services;
using EntraPimManager.Tests.TestSupport;

public sealed class GroupResolverTests
{
    [Fact]
    public async Task ResolveAsync_BatchResolvesGroupsAndDeduplicatesIds()
    {
        var handler = new FakeHttpMessageHandler(
            FakeHttpMessageHandler.JsonResponse(FixtureLoader.Load("groups-batch.json")));
        var resolver = new GroupResolver(GraphClientTestBuilder.Build(handler));

        var result = await resolver.ResolveAsync(["group-1", "group-2", "group-1"]);

        Assert.Equal(2, result.Count);
        Assert.True(result["group-1"].IsAssignableToRole);
        Assert.Equal("grp-project-x", result["group-2"].DisplayName);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ResolveAsync_WithNoIds_ReturnsEmptyWithoutCallingGraph()
    {
        var handler = new FakeHttpMessageHandler(FakeHttpMessageHandler.JsonResponse("{}"));
        var resolver = new GroupResolver(GraphClientTestBuilder.Build(handler));

        var result = await resolver.ResolveAsync([]);

        Assert.Empty(result);
        Assert.Equal(0, handler.RequestCount);
    }
}
