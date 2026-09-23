using Aynera.Application.Features.Liveness.Models;
using Aynera.Application.Features.Liveness.Repositories;
using Aynera.Application.Features.Liveness.Services.Implementations;
using Aynera.Application.Features.Liveness.Services.Interfaces;
using Aynera.Domain.Liveness.Enums;
using Aynera.Domain.Liveness.Exceptions;
using Aynera.Domain.Liveness.Records;
using Aynera.Domain.Photos.Enums;
using Microsoft.Extensions.Options;

namespace Aynera.Application.Tests;

/// <summary>
/// The verdict is the server's, read from the provider: these pin the confidence mark, that a pass
/// keeps the frame as the member's verified face, and that one member can never complete another's session.
/// </summary>
public sealed class LivenessServiceTests
{
    private static readonly Guid Member = Guid.NewGuid();

    private sealed class Harness
    {
        public ScriptedProvider Provider { get; } = new();
        public SessionStore Store { get; } = new();

        public LivenessService Service => new(
            Provider, Store,
            NoopAuditWriter.Instance,
            DiscardLogger<LivenessService>.Instance,
            Options.Create(new LivenessOptions { MinConfidence = 80m, PageUrl = "https://api.test/liveness/" }));
    }

    [Fact]
    public async Task Start_ReturnsThePageWithTheSessionRegionAndPool()
    {
        var h = new Harness();

        var start = await h.Service.StartAsync(Member, default);

        Assert.Equal("ap-south-1", start.Region);
        Assert.StartsWith("https://api.test/liveness/?session=", start.PageUrl);
        Assert.Contains("&region=ap-south-1", start.PageUrl);
        Assert.Contains("&pool=", start.PageUrl);
        Assert.Equal(LivenessOutcome.Pending.ToString(), h.Store.Get(start.SessionId).Outcome);
    }

    /// <summary>The face check comes first: it needs no photo, and nothing is compared.</summary>
    [Fact]
    public async Task Live_Passes_WithoutAnyPhoto_AndTheFrameBecomesTheVerifiedFace()
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);
        h.Provider.Result = new FaceLivenessResult("SUCCEEDED", 97.2m, [1, 2, 3]);

        var result = await h.Service.CompleteAsync(Member, start.SessionId, default);

        Assert.True(result.Passed);
        Assert.Equal("Passed", result.Outcome);
        Assert.Equal(FaceMatchStatus.Matched.ToString(), h.Store.SelfieStatus);
    }

    [Theory]
    [InlineData(79.9)]
    [InlineData(10)]
    public async Task BelowTheConfidenceMark_IsNotLive_AndNotAVerifiedFace(double confidence)
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);
        h.Provider.Result = new FaceLivenessResult("SUCCEEDED", (decimal)confidence, [1, 2, 3]);

        var result = await h.Service.CompleteAsync(Member, start.SessionId, default);

        Assert.False(result.Passed);
        Assert.Equal("NotLive", result.Outcome);
        // Kept for a curator, but never as a verified face.
        Assert.Equal(FaceMatchStatus.Pending.ToString(), h.Store.SelfieStatus);
    }

    [Theory]
    [InlineData("CREATED")]
    [InlineData("IN_PROGRESS")]
    public async Task NotFinished_IsAConflict_AndTheSessionStaysOpen(string status)
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);
        h.Provider.Result = new FaceLivenessResult(status, null, null);

        var ex = await Assert.ThrowsAsync<LivenessException>(() => h.Service.CompleteAsync(Member, start.SessionId, default));

        Assert.Equal("liveness_not_finished", ex.ErrorCode);
        Assert.Equal(LivenessOutcome.Pending.ToString(), h.Store.Get(start.SessionId).Outcome);
    }

    [Theory]
    [InlineData("EXPIRED", "Expired")]
    [InlineData("FAILED", "Failed")]
    public async Task ProviderEndStates_AreRecorded(string status, string outcome)
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);
        h.Provider.Result = new FaceLivenessResult(status, null, null);

        Assert.Equal(outcome, (await h.Service.CompleteAsync(Member, start.SessionId, default)).Outcome);
    }

    [Fact]
    public async Task AnotherMembersSession_ReadsAsNotFound()
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);

        var ex = await Assert.ThrowsAsync<LivenessException>(() => h.Service.CompleteAsync(Guid.NewGuid(), start.SessionId, default));

        Assert.Equal("liveness_session_not_found", ex.ErrorCode);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task CompletingTwice_ReturnsTheSameVerdict_WithoutAskingTheProviderAgain()
    {
        var h = new Harness();
        var start = await h.Service.StartAsync(Member, default);
        h.Provider.Result = new FaceLivenessResult("SUCCEEDED", 99m, [1, 2, 3]);

        var first = await h.Service.CompleteAsync(Member, start.SessionId, default);
        h.Provider.Result = new FaceLivenessResult("SUCCEEDED", 5m, [9]);
        var second = await h.Service.CompleteAsync(Member, start.SessionId, default);

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(1, h.Provider.ResultReads);
    }

    // ---- stand-ins -------------------------------------------------------------------------

    private sealed class ScriptedProvider : IFaceLivenessProvider
    {
        public FaceLivenessResult Result { get; set; } = new("SUCCEEDED", 99m, [1]);
        public int SessionsCreated { get; private set; }
        public int ResultReads { get; private set; }
        public string Region => "ap-south-1";
        public string IdentityPoolId => "ap-south-1:pool";

        public Task<string> CreateSessionAsync(CancellationToken cancellationToken)
        {
            SessionsCreated++;
            return Task.FromResult(Guid.NewGuid().ToString());
        }

        public Task<FaceLivenessResult> GetResultAsync(string sessionId, CancellationToken cancellationToken)
        {
            ResultReads++;
            return Task.FromResult(Result);
        }
    }

    private sealed class SessionStore : ILivenessRepository
    {
        private readonly Dictionary<string, LivenessSessionRecord> _sessions = new();
        public string? SelfieStatus { get; private set; }

        public LivenessSessionRecord Get(string id) => _sessions[id];

        public Task AddSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken)
        {
            _sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task<LivenessSessionRecord?> FindSessionAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

        public Task UpdateSessionAsync(LivenessSessionRecord session, CancellationToken cancellationToken)
        {
            _sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task<LivenessSessionRecord?> FindLatestCompletedAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_sessions.Values.Where(s => s.UserId == userId && s.CompletedAtUtc != null)
                .OrderByDescending(s => s.CompletedAtUtc).FirstOrDefault());

        public Task SaveSelfieAsync(Guid userId, byte[] jpeg, string faceMatchStatus, decimal? similarity, CancellationToken cancellationToken)
        {
            SelfieStatus = faceMatchStatus;
            return Task.CompletedTask;
        }
    }
}
