namespace Gateway.Auth;

public record LoginRequest(string Username, string Password);

public record TokenResponse(string Token, string Username, string Role, DateTime ExpiresAt);

// Bound from appsettings.json "Auth:Users" — predefined demo accounts, never in code.
public class UserCredential
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
}

public class AuthOptions
{
    public List<UserCredential> Users { get; set; } = [];
}
