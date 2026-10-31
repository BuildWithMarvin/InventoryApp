using System.Net;
using System.Net.Http.Json;
using InventoryApp.Api.Dtos;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class RateLimitTests : ApiTestBase
{
    public RateLimitTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Auth:LoginRequestsPerMinute"] = "3",
        ["Auth:EnrollRequestsPerMinute"] = "2",
    };

    [Fact]
    public async Task Login_IsLimitedPerClient_AndTheWindowResets()
    {
        var pda = await EnrollPdaAsync();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(pda, "NO-SUCH-BADGE")).StatusCode);

        var limited = await LoginAsync(pda, "NO-SUCH-BADGE");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Enroll_IsLimitedPerClient()
    {
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest("WRONGCODE"))).StatusCode);

        var limited = await Client.PostAsJsonAsync("/api/auth/enroll", new EnrollRequest("WRONGCODE"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task OtherEndpoints_AreNotLimitedByTheLoginLimit()
    {
        await CreateEmployeeAsync("Anna", "BADGE-1");
        var token = await LoginTokenAsync(await EnrollPdaAsync(), "BADGE-1");

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/products", token)).StatusCode);
    }
}
