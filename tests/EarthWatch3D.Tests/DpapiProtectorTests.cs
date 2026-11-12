using EarthWatch3D.App.Services;

namespace EarthWatch3D.Tests;

public class DpapiProtectorTests
{
    [Fact]
    public void RoundTripsForTheCurrentUserAndHidesThePlainText()
    {
        var p = new DpapiProtector();
        string blob = p.Protect("consumer-secret-123");
        Assert.DoesNotContain("consumer-secret-123", blob);
        Assert.Equal("consumer-secret-123", p.Unprotect(blob));
    }

    [Theory, InlineData("not base64 !!"), InlineData("AAAA"), InlineData("")]
    public void UndecryptableGivesNull(string blob) => Assert.Null(new DpapiProtector().Unprotect(blob));
}
