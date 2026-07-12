using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Auth;

public class TokenService(IOptions<AuthOptions> authOptions, string jwtSecret)
{
    public const string Issuer = "ApprovalFlow.Gateway";
    public const string Audience = "ApprovalFlow";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(8);

    public UserCredential? ValidateCredentials(string username, string password) =>
        authOptions.Value.Users.FirstOrDefault(u => u.Username == username && u.Password == password);

    public TokenResponse IssueToken(UserCredential user)
    {
        var expiresAt = DateTime.UtcNow.Add(TokenLifetime);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: signingCredentials);

        return new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), user.Username, user.Role, expiresAt);
    }
}
