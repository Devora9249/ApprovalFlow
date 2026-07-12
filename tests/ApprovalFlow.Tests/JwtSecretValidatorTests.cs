using Gateway.Auth;
using Xunit;

namespace ApprovalFlow.Tests;

public class JwtSecretValidatorTests
{
    [Fact]
    public void Validate_MissingSecret_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(null));
        Assert.Contains("not configured", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceSecret_Throws(string secret)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(secret));
        Assert.Contains("not configured", ex.Message);
    }

    [Fact]
    public void Validate_SecretShorterThanMinimum_Throws()
    {
        var shortSecret = new string('x', JwtSecretValidator.MinimumBytes - 1);

        var ex = Assert.Throws<InvalidOperationException>(() => JwtSecretValidator.Validate(shortSecret));
        Assert.Contains("at least", ex.Message);
    }

    [Fact]
    public void Validate_SecretAtMinimumLength_ReturnsSecret()
    {
        var secret = new string('x', JwtSecretValidator.MinimumBytes);

        Assert.Equal(secret, JwtSecretValidator.Validate(secret));
    }
}
