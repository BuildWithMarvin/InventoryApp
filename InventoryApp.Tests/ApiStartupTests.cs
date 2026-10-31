// Boots the real application in-process. It migrates the shared test database on
// startup, so it belongs to the shared collection and never runs in parallel with
// the other integration tests.
[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class ApiStartupTests
{
    [Fact]
    public async Task Application_StartsAndServesHttpRequests()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Application_RefusesToStart_WithoutAUsableSigningKey(string signingKey)
    {
        var factory = new ApiFactory(new Dictionary<string, string> { ["Auth:SigningKey"] = signingKey });

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("SigningKey", exception.ToString());
    }
}
