using Microsoft.AspNetCore.Hosting;

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

    [Fact]
    public async Task Swagger_IsServed_InDevelopment()
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(b => b.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        Assert.True((await client.GetAsync("/swagger/v1/swagger.json")).IsSuccessStatusCode);
        Assert.True((await client.GetAsync("/swagger/index.html")).IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Swagger_IsNotServed_OutsideDevelopment(string environment)
    {
        await using var factory = new ApiFactory().WithWebHostBuilder(b => b.UseEnvironment(environment));
        using var client = factory.CreateClient();

        foreach (var url in new[] { "/swagger/v1/swagger.json", "/swagger/index.html", "/swagger" })
        {
            var response = await client.GetAsync(url);

            Assert.False(response.IsSuccessStatusCode, url);
            Assert.DoesNotContain("openapi", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
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
