using StackExchange.Redis;
using Xunit;

namespace ContactConnection.Infrastructure.Tests.Credentials;

/// <summary>
/// Shared real Redis connection for the credential-cache and OAuth2-token-cache tests below.
/// Deliberately NOT mocked — StackExchange.Redis's IDatabase has many optional-parameter
/// overloads that are brittle to mock faithfully across versions, and this repo's documented dev
/// workflow already assumes `docker compose up -d` (the `cc_redis` service) is running before
/// `dotnet test`/`dotnet run` — see CLAUDE.md "Running the Stack". Connects to
/// `ConnectionStrings__Redis` if set, otherwise the same "localhost:6379" default
/// ServiceCollectionExtensions.AddInfrastructure falls back to.
///
/// Every test using this fixture must use a unique, randomized key (e.g. a Guid in the key name)
/// so parallel test runs never collide, and should clean up its own keys.
/// </summary>
public sealed class RedisFixture : IDisposable
{
    public IConnectionMultiplexer Connection { get; }

    public RedisFixture()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? "localhost:6379";
        try
        {
            Connection = ConnectionMultiplexer.Connect(connectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not connect to Redis at '{connectionString}' for RedisFixture-backed tests. " +
                "Run `docker compose up -d` (the cc_redis service) before running this test project.", ex);
        }
    }

    public void Dispose() => Connection.Dispose();
}

[CollectionDefinition("Redis")]
public class RedisCollection : ICollectionFixture<RedisFixture>;
