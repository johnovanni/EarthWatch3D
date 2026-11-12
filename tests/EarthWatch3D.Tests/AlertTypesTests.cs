using EarthWatch3D.Core.Events;

namespace EarthWatch3D.Tests;

public class AlertTypesTests
{
    [Theory]
    [InlineData("Tornado Warning", AlertTypes.Warnings)]
    [InlineData("Flood Watch", AlertTypes.Watches)]
    [InlineData("Small Craft Advisory", AlertTypes.Advisories)]
    [InlineData("Special Weather Statement", AlertTypes.Statements)]
    [InlineData("Air Quality Alert", AlertTypes.Statements)]
    public void ClassifiesBySuffix(string type, AlertTypes expected) =>
        Assert.Equal(expected, AlertTypeClassifier.Classify(type));

    [Theory]
    [InlineData(33.0, -1)]
    [InlineData(34.0, 0)]
    [InlineData(63.9, 0)]
    [InlineData(64.0, 1)]
    [InlineData(83.0, 2)]
    [InlineData(96.0, 3)]
    [InlineData(113.0, 4)]
    [InlineData(137.0, 5)]
    public void SaffirSimpsonBoundaries(double kt, int cat) => Assert.Equal(cat, SaffirSimpson.Category(kt));

    [Fact]
    public void NullWindIsDepression() => Assert.Equal(-1, SaffirSimpson.Category(null));
}
