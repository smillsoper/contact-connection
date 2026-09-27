using ContactConnection.Infrastructure.Common;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Common;

public class ApiKeyHasherTests
{
    [Fact]
    public void Issue_ReturnsPrefixedRandomKey_WithMatchingHash()
    {
        var (plain, hash, display) = ApiKeyHasher.Issue();
        var (plain2, _, _) = ApiKeyHasher.Issue();

        Assert.StartsWith(ApiKeyHasher.KeyPrefix, plain);
        Assert.True(plain.Length > 40);
        Assert.NotEqual(plain, plain2);
        Assert.Equal(ApiKeyHasher.Hash(plain), hash);
        Assert.Equal(64, hash.Length);                 // SHA-256 hex
        Assert.Equal(plain[..12], display);
        Assert.DoesNotContain('+', plain);             // URL/header safe
        Assert.DoesNotContain('/', plain);
    }

    [Fact]
    public void Hash_IsDeterministic_AndCaseSensitive()
    {
        Assert.Equal(ApiKeyHasher.Hash("ccrk_abc"), ApiKeyHasher.Hash("ccrk_abc"));
        Assert.NotEqual(ApiKeyHasher.Hash("ccrk_abc"), ApiKeyHasher.Hash("ccrk_ABC"));
    }
}
