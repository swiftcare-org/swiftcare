using System.IdentityModel.Tokens.Jwt;
using System.Text;
using AuthService.Models.Configuration;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.UnitTests.Services;

public class JwtTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-must-be-at-least-32-bytes-long";

    private static JwtTokenService CreateService(int expiryHours = 12)
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = SigningKey,
            Issuer = "SwiftCare.AuthService.Tests",
            Audience = "SwiftCare.Tests",
            ExpiryHours = expiryHours
        });

        return new JwtTokenService(options);
    }

    private static User CreateUser(UserRole role, string? roomNumber = null) => new()
    {
        Id = Guid.NewGuid(),
        Username = "test.user",
        PasswordHash = "irrelevant-for-token-generation",
        FullName = "Test User",
        Role = role,
        RoomNumber = roomNumber
    };

    [Fact]
    public void GenerateTokenForDoctorIncludesRoomNumberClaim()
    {
        var service = CreateService();
        var user = CreateUser(UserRole.Doctor, "R-204");

        var (token, _) = service.GenerateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.FullName, jwt.Claims.Single(c => c.Type == "fullName").Value);
        Assert.Equal(nameof(UserRole.Doctor), jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("R-204", jwt.Claims.Single(c => c.Type == "roomNumber").Value);
    }

    [Theory]
    [InlineData(UserRole.Receptionist)]
    [InlineData(UserRole.Admin)]
    public void GenerateTokenForNonDoctorRoleOmitsRoomNumberClaim(UserRole role)
    {
        var service = CreateService();
        var user = CreateUser(role);

        var (token, _) = service.GenerateToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.DoesNotContain(jwt.Claims, c => c.Type == "roomNumber");
    }

    [Fact]
    public void GenerateTokenSetsExpiryTwelveHoursOut()
    {
        var service = CreateService(expiryHours: 12);
        var user = CreateUser(UserRole.Admin);

        var beforeCall = DateTime.UtcNow;
        var (_, expiresAt) = service.GenerateToken(user);
        var afterCall = DateTime.UtcNow;

        Assert.InRange(expiresAt, beforeCall.AddHours(12), afterCall.AddHours(12).AddSeconds(1));
    }

    [Fact]
    public void GenerateTokenProducesTokenThatValidatesAgainstConfiguredParameters()
    {
        var service = CreateService();
        var user = CreateUser(UserRole.Receptionist);

        var (token, _) = service.GenerateToken(user);

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = "SwiftCare.AuthService.Tests",
            ValidAudience = "SwiftCare.Tests",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out var validatedToken);

        Assert.NotNull(principal);
        Assert.IsType<JwtSecurityToken>(validatedToken);
    }

    [Fact]
    public void GenerateTokenThrowsWhenSigningKeyIsUnderThirtyTwoBytes()
    {
        var options = Options.Create(new JwtOptions
        {
            SecretKey = "too-short-key",
            Issuer = "SwiftCare.AuthService.Tests",
            Audience = "SwiftCare.Tests"
        });
        var service = new JwtTokenService(options);
        var user = CreateUser(UserRole.Admin);

        Assert.ThrowsAny<Exception>(() => service.GenerateToken(user));
    }
}
