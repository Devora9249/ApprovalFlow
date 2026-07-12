using System.Text;

namespace Gateway.Auth;

// HS256 signing fails with a cryptic IDX10653 deep in the JWT library if the key is
// too short. Validate up front at startup so a missing/weak JWT_SECRET fails fast
// with a clear message instead of crashing on the first login attempt.
public static class JwtSecretValidator
{
    public const int MinimumBytes = 32;

    public static string Validate(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Jwt:Secret is not configured (set JWT_SECRET).");

        if (Encoding.UTF8.GetByteCount(secret) < MinimumBytes)
            throw new InvalidOperationException($"Jwt:Secret must be at least {MinimumBytes} bytes long (set JWT_SECRET to a longer value).");

        return secret;
    }
}
