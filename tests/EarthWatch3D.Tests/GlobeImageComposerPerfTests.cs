using EarthWatch3D.Core.Globe;

namespace EarthWatch3D.Tests;

/// <summary>Timing tests run on their own: run beside the other tests, they compete for the CPU and miss their budget.</summary>
[CollectionDefinition("Perf", DisableParallelization = true)]
public sealed class PerfCollection;

[Collection("Perf")]
public class GlobeImageComposerPerfTests
{
    [Fact, Trait("Category", "Perf")]
    public void Composes8192UnderBudget()
    {
        var base8192 = Bgra32Image.Create(8192, 4096);
        for (int i = 0; i < base8192.Pixels.Length; i += 4) { base8192.Pixels[i] = base8192.Pixels[i+1] = base8192.Pixels[i+2] = 128; base8192.Pixels[i+3] = 255; }
        var composer = new GlobeImageComposer(base8192);
        var target = Bgra32Image.Create(8192, 4096);
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55), target);  // warm-up
        var sw = System.Diagnostics.Stopwatch.StartNew();
        composer.ComposeInto(new GlobeLayers(new(10, 20), 0.55, Lines: Bgra32Image.Create(8192, 4096)), target);
        Assert.True(sw.ElapsedMilliseconds < 300, $"{sw.ElapsedMilliseconds} ms");
    }
}
