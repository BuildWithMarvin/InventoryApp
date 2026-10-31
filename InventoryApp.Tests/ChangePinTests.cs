using System.Net;
using InventoryApp.Api.Dtos;
using InventoryApp.Api.Models;
using InventoryApp.Api.Security;
using Microsoft.EntityFrameworkCore;

[Trait("Category", "Integration")]
[Collection("Integration Tests")]
public class ChangePinTests : ApiTestBase
{
    private Pda _pda = null!;

    public ChangePinTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await CreateEmployeeAsync("Anna", "BADGE-1", pin: "123456");
        await RequirePinAsync();
        _pda = await EnrollPdaAsync();
    }

    private Task<HttpResponseMessage> ChangePinAsync(string token, string? current, string next) =>
        SendAsync(HttpMethod.Post, "/api/auth/change-pin", new ChangePinRequest(current, next), token);

    [Fact]
    public async Task ChangePin_ReplacesThePin()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1", "123456");

        var response = await ChangePinAsync(token, "123456", "654321");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_pda, "BADGE-1", "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_pda, "BADGE-1", "654321")).StatusCode);

        await using var context = CreateContextFor(Tenant);
        var anna = await context.Employees.SingleAsync();
        Assert.False(anna.MustChangePin);
        Assert.DoesNotContain("654321", anna.PinHash);
    }

    [Fact]
    public async Task ChangePin_EndsTheOtherSessionsOfTheEmployee_ButNotTheCurrentOne()
    {
        var otherPda = await EnrollPdaAsync("PDA 2");
        var currentToken = await LoginTokenAsync(_pda, "BADGE-1", "123456");
        var otherToken = await LoginTokenAsync(otherPda, "BADGE-1", "123456");

        await ChangePinAsync(currentToken, "123456", "654321");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync("/api/products", currentToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/products", otherToken)).StatusCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("")]
    public async Task ChangePin_RejectsAPinThatIsNotSixDigits(string next)
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1", "123456");

        var response = await ChangePinAsync(token, "123456", next);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_pda, "BADGE-1", "123456")).StatusCode);
    }

    [Fact]
    public async Task ChangePin_RejectsTheSamePin()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1", "123456");

        Assert.Equal(HttpStatusCode.BadRequest, (await ChangePinAsync(token, "123456", "123456")).StatusCode);
    }

    [Fact]
    public async Task ChangePin_WithAWrongCurrentPin_IsRefused_AndCountsLikeALoginAttempt()
    {
        var token = await LoginTokenAsync(_pda, "BADGE-1", "123456");

        for (var i = 0; i < Employee.MaxFailedPinAttempts; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await ChangePinAsync(token, "000000", "654321")).StatusCode);

        // Locked now: even the right current PIN is refused, and so is a login.
        Assert.Equal(HttpStatusCode.Locked, (await ChangePinAsync(token, "123456", "654321")).StatusCode);
        Assert.Equal(HttpStatusCode.Locked, (await LoginAsync(_pda, "BADGE-1", "123456")).StatusCode);
    }

    [Fact]
    public async Task ChangePin_AnEmployeeWithoutAPin_CanSetTheFirstOne()
    {
        await RequirePinAsync(false);
        await CreateEmployeeAsync("Ben", "BADGE-2");
        var token = await LoginTokenAsync(_pda, "BADGE-2");

        var response = await ChangePinAsync(token, null, "222222");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await RequirePinAsync();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_pda, "BADGE-2", "222222")).StatusCode);
    }
}
