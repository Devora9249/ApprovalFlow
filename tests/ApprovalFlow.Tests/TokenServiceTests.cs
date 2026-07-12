using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Gateway.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApprovalFlow.Tests;

public class TokenServiceTests
{
    private static readonly List<UserCredential> Users =
    [
        new() { Username = "dana", Password = "pass123", Role = "submitter" },
        new() { Username = "manager1", Password = "pass456", Role = "approver" },
        new() { Username = "admin", Password = "pass789", Role = "admin" }
    ];

    private static TokenService BuildService(string jwtSecret = "test-secret-at-least-32-bytes-long!") =>
        new(Options.Create(new AuthOptions { Users = Users }), jwtSecret);

    [Theory]
    [InlineData("dana", "pass123", "submitter")]
    [InlineData("manager1", "pass456", "approver")]
    [InlineData("admin", "pass789", "admin")]
    public void ValidateCredentials_KnownUserCorrectPassword_ReturnsUserWithRole(string username, string password, string role)
    {
        var user = BuildService().ValidateCredentials(username, password);

        Assert.NotNull(user);
        Assert.Equal(role, user!.Role);
    }

    [Fact]
    public void ValidateCredentials_WrongPassword_ReturnsNull()
    {
        var user = BuildService().ValidateCredentials("dana", "wrong-password");

        Assert.Null(user);
    }

    [Fact]
    public void ValidateCredentials_UnknownUsername_ReturnsNull()
    {
        var user = BuildService().ValidateCredentials("nobody", "pass123");

        Assert.Null(user);
    }

    [Fact]
    public void IssueToken_ProducesJwtWithNameRoleClaimsAndFutureExpiry()
    {
        var service = BuildService();
        var user = Users.Single(u => u.Username == "manager1");

        var response = service.IssueToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);

        Assert.Equal("manager1", jwt.Claims.Single(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal("approver", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Equal(TokenService.Issuer, jwt.Issuer);
        Assert.Contains(TokenService.Audience, jwt.Audiences);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        Assert.Equal("approver", response.Role);
        Assert.Equal("manager1", response.Username);
    }
}
