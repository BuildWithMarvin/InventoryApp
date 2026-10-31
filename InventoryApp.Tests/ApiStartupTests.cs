using Microsoft.AspNetCore.Mvc.Testing;

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
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.True(response.IsSuccessStatusCode);
    }
}
