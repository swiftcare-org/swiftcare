using System.Net;
using System.Net.Http.Json;
using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuthService.UnitTests.Controllers;

public sealed class InternalSessionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("incorrect")]
    public async Task SessionAuthorityRequiresTheGatewaySecret(string? secret)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        using var client = factory.CreateClient();
        if (secret is not null) client.DefaultRequestHeaders.Add("X-Gateway-Secret", secret);
        using var response = await client.PostAsJsonAsync("/internal/sessions/validate",
            new SessionValidationRequest(Guid.NewGuid(), Guid.NewGuid(), "token", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), false));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TrustedAuthorityPersistsLogoutAndRejectsReplay()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var user = new User { Username = "test-session", PasswordHash = "hash", FullName = "Test User", Role = UserRole.Doctor };
        await using (var context = new AuthDbContext(options))
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        using var factory = new AuthServiceWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<AuthDbContext>();
            services.AddScoped(_ => new AuthDbContext(options));
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Gateway-Secret", AuthServiceWebApplicationFactory.ValidGatewaySecret);
        var request = new SessionValidationRequest(user.Id, user.SessionVersion, "persisted-token",
            DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), false);
        using (var valid = await client.PostAsJsonAsync("/internal/sessions/validate", request))
        {
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
            Assert.True(await valid.Content.ReadFromJsonAsync<bool>());
        }
        using (var logout = await client.PostAsJsonAsync("/internal/sessions/validate", request with { Revoke = true }))
            Assert.True(await logout.Content.ReadFromJsonAsync<bool>());
        using var replay = await client.PostAsJsonAsync("/internal/sessions/validate", request);
        Assert.False(await replay.Content.ReadFromJsonAsync<bool>());
        await using var reader = new AuthDbContext(options);
        Assert.Equal(request.TokenId, (await reader.RevokedSessions.SingleAsync()).TokenId);
    }
}
