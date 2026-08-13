using AutoMapper;
using Elaris.Application.Features.Auth.Repositories;
using Elaris.Application.Features.Feedback.Models;
using Elaris.Application.Features.Feedback.Repositories;
using Elaris.Application.Features.Feedback.Services.Interfaces;
using Elaris.Application.Features.PublicForms.Repositories;
using Elaris.Domain.Feedback.Records;
using Elaris.Domain.Feedback.Requests;
using Elaris.Domain.Feedback.Responses;
using Elaris.Domain.Feedback.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elaris.Application.Features.Feedback.Services.Implementations;

public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackSubmissionRepository _feedback;
    private readonly IUserRepository _users;
    private readonly IPublicFormRateLimiter _rateLimiter;
    private readonly IMapper _mapper;
    private readonly ILogger<FeedbackService> _logger;
    private readonly FeedbackOptions _options;

    public FeedbackService(
        IFeedbackSubmissionRepository feedback,
        IUserRepository users,
        IPublicFormRateLimiter rateLimiter,
        IMapper mapper,
        ILogger<FeedbackService> logger,
        IOptions<FeedbackOptions> options)
    {
        _feedback = feedback;
        _users = users;
        _rateLimiter = rateLimiter;
        _mapper = mapper;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<FeedbackSubmissionDto> SubmitAsync(
        SubmitFeedbackRequest request,
        string? clientIp,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SubmitFeedback starting");
        var message = request.Message.Trim();
        if (message.Length > _options.MaxMessageLength)
        {
            throw new FeedbackException(
                "feedback_message_too_long",
                $"Message must be at most {_options.MaxMessageLength} characters.");
        }

        var fullName = request.FullName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        var (allowed, _) = await _rateLimiter.TryAcquireAsync(
            "feedback",
            clientIp,
            _options.MaxRequestsPerIpPerHour,
            cancellationToken);
        if (!allowed)
        {
            throw new FeedbackException(
                "feedback_rate_limited",
                "Too many feedback submissions from this network. Please try again later.",
                statusCode: 429);
        }

        var existingUser = await _users.FindByEmailAsync(email, cancellationToken);
        var isExistingUser = existingUser is not null;

        var created = await _feedback.AddAsync(
            new FeedbackSubmissionRecord(
                Id: Guid.NewGuid(),
                FullName: fullName,
                Email: email,
                Phone: phone,
                Message: message,
                ClientIp: Truncate(clientIp, 64),
                UserAgent: Truncate(userAgent, 512),
                IsExistingUser: isExistingUser,
                CreatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        return _mapper.Map<FeedbackSubmissionDto>(created);
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= max
                ? value
                : value[..max];
}
