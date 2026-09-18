using Aynera.Application.Features.Auth.Models;
using Aynera.Application.Features.Auth.Repositories;
using Aynera.Domain.Auth.Records;
using Aynera.Domain.Auth.Statics;
using Aynera.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Aynera.Application.Tests;

public sealed class OtpChallengeRepositoryTests
{
    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TryConsume_AllowsExactlyOneSuccessfulConsume(string store)
    {
        var (repo, dispose) = Create(store);
        using var _ = dispose;
        var destination = UniqueDestination();
        var hash = TokenHasher.Hash("123456");
        await repo.StoreAsync(Challenge(destination, hash), TimeSpan.FromMinutes(5), CancellationToken.None);

        var outcomes = await Task.WhenAll(
            repo.TryConsumeAsync("phone", destination, hash, "login", "member", CancellationToken.None),
            repo.TryConsumeAsync("phone", destination, hash, "login", "member", CancellationToken.None));

        Assert.Equal(1, outcomes.Count(o => o == OtpConsumeOutcome.Consumed));
        Assert.Equal(1, outcomes.Count(o => o == OtpConsumeOutcome.NotFound));
        Assert.Null(await repo.GetAsync("phone", destination, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TryConsume_IncrementsConcurrentWrongAttemptsWithoutLosingCounts(string store)
    {
        var (repo, dispose) = Create(store, maxAttempts: 5);
        using var _ = dispose;
        var destination = UniqueDestination();
        var hash = TokenHasher.Hash("123456");
        await repo.StoreAsync(Challenge(destination, hash), TimeSpan.FromMinutes(5), CancellationToken.None);
        var wrong = TokenHasher.Hash("000000");

        var outcomes = await Task.WhenAll(
            repo.TryConsumeAsync("phone", destination, wrong, "login", "member", CancellationToken.None),
            repo.TryConsumeAsync("phone", destination, wrong, "login", "member", CancellationToken.None));

        Assert.All(outcomes, outcome => Assert.Equal(OtpConsumeOutcome.Invalid, outcome));
        var remaining = await repo.GetAsync("phone", destination, CancellationToken.None);
        Assert.NotNull(remaining);
        Assert.Equal(2, remaining.Attempts);
        Assert.Equal(hash, remaining.CodeHash);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TryConsume_RejectsExpiredWrongPurposeAndWrongAudience(string store)
    {
        var (repo, dispose) = Create(store);
        using var _ = dispose;
        var destination = UniqueDestination();
        var hash = TokenHasher.Hash("123456");
        await repo.StoreAsync(
            Challenge(destination, hash) with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-2) },
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.Equal(
            OtpConsumeOutcome.NotFound,
            await repo.TryConsumeAsync("phone", destination, hash, "login", "member", CancellationToken.None));

        destination = UniqueDestination();
        await repo.StoreAsync(Challenge(destination, hash, purpose: "login"), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(
            OtpConsumeOutcome.NotFound,
            await repo.TryConsumeAsync("phone", destination, hash, "reactivation", "member", CancellationToken.None));
        Assert.NotNull(await repo.GetAsync("phone", destination, CancellationToken.None));

        destination = UniqueDestination();
        await repo.StoreAsync(Challenge(destination, hash, audience: "member"), TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.Equal(
            OtpConsumeOutcome.NotFound,
            await repo.TryConsumeAsync("phone", destination, hash, "login", "admin", CancellationToken.None));
        Assert.NotNull(await repo.GetAsync("phone", destination, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TryConsume_DoesNotRemoveReplacementChallenge(string store)
    {
        var (repo, dispose) = Create(store);
        using var _ = dispose;
        var destination = UniqueDestination();
        var oldHash = TokenHasher.Hash("111111");
        var newHash = TokenHasher.Hash("222222");
        await repo.StoreAsync(Challenge(destination, oldHash), TimeSpan.FromMinutes(5), CancellationToken.None);
        var replacement = Challenge(destination, newHash) with { ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        await repo.StoreAsync(replacement, TimeSpan.FromMinutes(10), CancellationToken.None);

        var stale = await repo.TryConsumeAsync("phone", destination, oldHash, "login", "member", CancellationToken.None);
        Assert.Equal(OtpConsumeOutcome.Invalid, stale);
        var current = await repo.GetAsync("phone", destination, CancellationToken.None);
        Assert.NotNull(current);
        Assert.Equal(newHash, current.CodeHash);
        Assert.Equal(1, current.Attempts);

        Assert.Equal(
            OtpConsumeOutcome.Consumed,
            await repo.TryConsumeAsync("phone", destination, newHash, "login", "member", CancellationToken.None));
        Assert.Null(await repo.GetAsync("phone", destination, CancellationToken.None));
        Assert.Equal(
            OtpConsumeOutcome.NotFound,
            await repo.TryConsumeAsync("phone", destination, newHash, "login", "member", CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task TryConsume_LocksOnExhaustedAttemptsAndDoesNotResurrect(string store)
    {
        var (repo, dispose) = Create(store, maxAttempts: 2);
        using var _ = dispose;
        var destination = UniqueDestination();
        var hash = TokenHasher.Hash("123456");
        await repo.StoreAsync(Challenge(destination, hash), TimeSpan.FromMinutes(5), CancellationToken.None);
        var wrong = TokenHasher.Hash("000000");

        Assert.Equal(
            OtpConsumeOutcome.Invalid,
            await repo.TryConsumeAsync("phone", destination, wrong, "login", "member", CancellationToken.None));
        Assert.Equal(
            OtpConsumeOutcome.Locked,
            await repo.TryConsumeAsync("phone", destination, wrong, "login", "member", CancellationToken.None));
        Assert.Null(await repo.GetAsync("phone", destination, CancellationToken.None));
        Assert.Equal(
            OtpConsumeOutcome.NotFound,
            await repo.TryConsumeAsync("phone", destination, hash, "login", "member", CancellationToken.None));
    }

    public static TheoryData<string> Stores()
    {
        var data = new TheoryData<string> { "memory" };
        if (RedisAvailable())
        {
            data.Add("redis");
        }

        return data;
    }

    private static (IOtpChallengeRepository Repo, IDisposable Dispose) Create(string store, int maxAttempts = 5)
    {
        var options = Options.Create(new OtpOptions
        {
            MaxAttempts = maxAttempts,
            TtlSeconds = 300,
            CodeLength = 6
        });
        if (store == "memory")
        {
            return (new InMemoryOtpChallengeRepository(options), NullDispose.Instance);
        }

        var mux = ConnectionMultiplexer.Connect(RedisConfiguration());
        return (new RedisOtpChallengeRepository(mux, options), mux);
    }

    private static OtpChallenge Challenge(
        string destination,
        string codeHash,
        string purpose = "login",
        string audience = "member") =>
        new("phone", destination, codeHash, 0, DateTimeOffset.UtcNow.AddMinutes(5), purpose, audience);

    private static string UniqueDestination() => "+91" + Random.Shared.NextInt64(100_000_0000, 9_999_999_999);

    private static string RedisConfiguration() =>
        Environment.GetEnvironmentVariable("AYNERA_REDIS") ?? "localhost:6379";

    private static bool RedisAvailable()
    {
        try
        {
            var options = ConfigurationOptions.Parse(RedisConfiguration());
            options.AbortOnConnectFail = true;
            options.ConnectTimeout = 1000;
            options.SyncTimeout = 1000;
            using var mux = ConnectionMultiplexer.Connect(options);
            return mux.IsConnected;
        }
        catch
        {
            return false;
        }
    }

    private sealed class NullDispose : IDisposable
    {
        public static readonly NullDispose Instance = new();
        public void Dispose()
        {
        }
    }
}
