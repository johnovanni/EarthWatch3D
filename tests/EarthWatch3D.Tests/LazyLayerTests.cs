using EarthWatch3D.App.Controllers;

namespace EarthWatch3D.Tests;

public sealed class LazyLayerTests
{
    private sealed class Builder
    {
        public int Calls;
        public int FailuresLeft;
        public Task<string> Build()
        {
            Calls++;
            if (FailuresLeft > 0) { FailuresLeft--; return Task.FromException<string>(new InvalidOperationException("bad asset")); }
            return Task.FromResult("layer");
        }
    }

    [Fact]
    public async Task BuildsOnceAndKeepsIt()
    {
        var b = new Builder();
        var layer = new LazyLayer<string>(b.Build);
        Assert.Equal("layer", await layer.GetAsync(_ => Assert.Fail("no failure expected")));
        Assert.Equal("layer", await layer.GetAsync(_ => Assert.Fail("no failure expected")));
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public async Task FailedLoadIsRememberedUntilTheLayerIsSwitchedOff()
    {
        var b = new Builder { FailuresLeft = 1 };
        var layer = new LazyLayer<string>(b.Build);
        var failures = new List<Exception>();
        for (int compose = 0; compose < 5; compose++)   // every compose asks for the layer
            Assert.Null(await layer.GetAsync(failures.Add));
        Assert.Equal(1, b.Calls);                          // not retried on every compose
        Assert.Single(failures);                           // and logged once
        Assert.True(layer.HasFailed);

        layer.ForgetFailure();                             // the user switched plates off (and on again)
        Assert.Equal("layer", await layer.GetAsync(failures.Add));
        Assert.Equal(2, b.Calls);
        Assert.False(layer.HasFailed);
    }
}
